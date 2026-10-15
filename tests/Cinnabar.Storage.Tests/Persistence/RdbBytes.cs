using System.Buffers.Binary;
using System.Text;

namespace Cinnabar.Storage.Tests.Persistence;

/// <summary>Builds RDB files by hand, for testing encodings <c>RdbWriter</c> never produces.</summary>
internal sealed class RdbBytes
{
    private readonly List<byte> _bytes = [];

    public RdbBytes(string version = "0009")
    {
        Raw(Encoding.ASCII.GetBytes("REDIS" + version));
    }

    public RdbBytes Raw(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    /// <summary>A length-prefixed string (6-bit length form).</summary>
    public RdbBytes Str(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length >= 64)
        {
            throw new ArgumentException("Use short strings in hand-built files.");
        }

        _bytes.Add((byte)bytes.Length);
        _bytes.AddRange(bytes);
        return this;
    }

    public RdbBytes SelectDb(byte db) => Raw(0xFE, db);

    public RdbBytes StringKey(string key, string value) => Raw(0x00).Str(key).Str(value);

    /// <summary>A string key whose value bytes are already encoded (e.g. 0xC0 ...).</summary>
    public RdbBytes StringKeyEncoded(string key, params byte[] encodedValue) => Raw(0x00).Str(key).Raw(encodedValue);

    public RdbBytes ExpireMs(long unixMs)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, unixMs);
        return Raw(0xFC).Raw(buffer);
    }

    public RdbBytes ExpireSeconds(int unixSeconds)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, unixSeconds);
        return Raw(0xFD).Raw(buffer);
    }

    /// <summary>Ends the file with EOF and a checksum of 0 (checksums disabled).</summary>
    public byte[] EndWithZeroChecksum() => Raw(0xFF).Raw(new byte[8]).ToArray();

    /// <summary>Ends the file with EOF and the real (Redis) CRC-64 checksum.</summary>
    public byte[] EndWithRedisChecksum()
    {
        Raw(0xFF);
        ulong crc = Crc64Jones.Compute(_bytes.ToArray());
        var buffer = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, crc);
        return Raw(buffer).ToArray();
    }

    /// <summary>Ends the file with EOF and no checksum (RDB versions before 5).</summary>
    public byte[] EndWithoutChecksum() => Raw(0xFF).ToArray();

    public byte[] ToArray() => _bytes.ToArray();
}

/// <summary>
/// Independent, bit-by-bit CRC-64/Jones (the variant Redis uses), written straight from the
/// Rocksoft model parameters: width 64, poly 0xad93d23594c935a9, init 0, refin true,
/// refout true, xorout 0, check 0xe9c6d914c4b8d9ca.
/// </summary>
internal static class Crc64Jones
{
    private const ulong Poly = 0xad93d23594c935a9UL;

    public static ulong Compute(ReadOnlySpan<byte> data)
    {
        ulong crc = 0;
        foreach (byte b in data)
        {
            // refin: feed each byte least-significant bit first into an MSB-first register.
            byte reflected = ReflectByte(b);
            crc ^= (ulong)reflected << 56;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000_0000_0000_0000UL) != 0 ? (crc << 1) ^ Poly : crc << 1;
            }
        }

        // refout
        return Reflect64(crc);
    }

    private static byte ReflectByte(byte value)
    {
        byte result = 0;
        for (int i = 0; i < 8; i++)
        {
            if ((value & (1 << i)) != 0)
            {
                result |= (byte)(1 << (7 - i));
            }
        }

        return result;
    }

    private static ulong Reflect64(ulong value)
    {
        ulong result = 0;
        for (int i = 0; i < 64; i++)
        {
            if ((value & (1UL << i)) != 0)
            {
                result |= 1UL << (63 - i);
            }
        }

        return result;
    }
}
