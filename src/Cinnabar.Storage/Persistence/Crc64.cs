using System.Buffers.Binary;

namespace Cinnabar.Storage.Persistence;

/// <summary>
/// CRC-64/Jones — the same variant Redis uses for RDB integrity.
/// Polynomial: 0xad93d23594c935a9
/// </summary>
internal static class Crc64
{
    // The table is built with right shifts (the reflected algorithm), so it needs the
    // bit-reversed form of the Jones polynomial 0xad93d23594c935a9.
    private const ulong Polynomial = 0x95AC9329AC4BC9B5UL;
    private static readonly ulong[] Table = BuildTable();

    private static ulong[] BuildTable()
    {
        var table = new ulong[256];

        for (uint i = 0; i < 256; i++)
        {
            ulong crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) == 1
                   ? (crc >> 1) ^ Polynomial
                   : crc >> 1;
            }
            table[i] = crc;
        }

        return table;
    }

    public static ulong Compute(ReadOnlySpan<byte> data)
    {
        ulong crc = 0;
        foreach (byte b in data)
        {
            crc = Table[(byte)(crc ^ b)] ^ (crc >> 8);
        }
        return crc;
    }

    /// <summary>
    /// Returns true if the last 8 bytes of <paramref name="rdbBytes"/> match
    /// the CRC-64 of everything before them.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> rdbBytes)
    {
        if (rdbBytes.Length < 8)
        {
            return false;
        }

        var payload = rdbBytes[..^8];
        var storedBytes = rdbBytes[^8..];

        ulong stored = BinaryPrimitives.ReadUInt64LittleEndian(storedBytes);
        ulong computed = Compute(payload);

        return stored == computed;
    }

    /// <summary>
    /// Appends an 8-byte little-endian CRC-64 checksum to the payload.
    /// Use this when writing RDB files.
    /// </summary>
    public static byte[] AppendChecksum(byte[] payload)
    {
        ulong crc = Compute(payload);
        var result = new byte[payload.Length + 8];
        payload.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(
            result.AsSpan(payload.Length), crc);
        return result;
    }
}
