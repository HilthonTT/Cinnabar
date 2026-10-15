using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Cinnabar.Storage.Persistence;

/// <summary>
/// Writes keys to an RDB snapshot (format version 9) that <see cref="RdbReader"/> and
/// Redis itself can load. Strings are written as type 0 and lists as plain lists (type 1).
/// </summary>
/// <remarks>
/// Streams are not written: the Redis stream encoding is out of scope. They are counted in
/// <see cref="RdbWriteResult.SkippedKeys"/> so callers can warn about it.
/// </remarks>
public static class RdbWriter
{
    private const string Version = "0009";

    /// <summary>
    /// Writes a snapshot to <paramref name="path"/> atomically: the data goes to a temporary
    /// file which then replaces the target, so a crash never leaves a half-written file.
    /// </summary>
    public static async Task<RdbWriteResult> WriteFileAsync(
        string path,
        IEnumerable<KeySnapshot> keys,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = Write(keys, out RdbWriteResult result);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
        return result;
    }

    /// <summary>Encodes a snapshot of database 0.</summary>
    public static byte[] Write(IEnumerable<KeySnapshot> keys, out RdbWriteResult result)
    {
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(Encoding.ASCII.GetBytes("REDIS" + Version));

        WriteAux(writer, "cinnabar-ver", typeof(RdbWriter).Assembly.GetName().Version?.ToString() ?? "0");
        WriteAux(writer, "ctime", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        WriteByte(writer, 0xFE);
        WriteLength(writer, 0);

        int written = 0;
        int skipped = 0;

        foreach (KeySnapshot key in keys)
        {
            if (key.Type is not (KeyType.String or KeyType.List))
            {
                skipped++;
                continue;
            }

            if (key.ExpiresAt is { } expiresAt)
            {
                WriteByte(writer, 0xFC);
                Span<byte> ms = writer.GetSpan(8);
                BinaryPrimitives.WriteInt64LittleEndian(ms, expiresAt.ToUnixTimeMilliseconds());
                writer.Advance(8);
            }

            if (key.Type == KeyType.String)
            {
                WriteByte(writer, 0x00);
                WriteString(writer, key.Key);
                WriteString(writer, (string)key.Value);
            }
            else
            {
                var items = (IReadOnlyCollection<string>)key.Value;
                WriteByte(writer, 0x01);
                WriteString(writer, key.Key);
                WriteLength(writer, items.Count);
                foreach (string item in items)
                {
                    WriteString(writer, item);
                }
            }

            written++;
        }

        WriteByte(writer, 0xFF);
        ulong crc = Crc64.Compute(writer.WrittenSpan);
        Span<byte> checksum = writer.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(checksum, crc);
        writer.Advance(8);

        result = new RdbWriteResult(written, skipped);
        return writer.WrittenSpan.ToArray();
    }

    private static void WriteByte(ArrayBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    private static void WriteAux(ArrayBufferWriter<byte> writer, string name, string value)
    {
        WriteByte(writer, 0xFA);
        WriteString(writer, name);
        WriteString(writer, value);
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteLength(writer, bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteLength(ArrayBufferWriter<byte> writer, long length)
    {
        if (length < 1 << 6)
        {
            WriteByte(writer, (byte)length);
        }
        else if (length < 1 << 14)
        {
            WriteByte(writer, (byte)(0x40 | (length >> 8)));
            WriteByte(writer, (byte)length);
        }
        else if (length <= uint.MaxValue)
        {
            Span<byte> span = writer.GetSpan(5);
            span[0] = 0x80;
            BinaryPrimitives.WriteUInt32BigEndian(span[1..], (uint)length);
            writer.Advance(5);
        }
        else
        {
            Span<byte> span = writer.GetSpan(9);
            span[0] = 0x81;
            BinaryPrimitives.WriteUInt64BigEndian(span[1..], (ulong)length);
            writer.Advance(9);
        }
    }
}

/// <summary>Outcome of an RDB write.</summary>
/// <param name="WrittenKeys">Keys written to the file.</param>
/// <param name="SkippedKeys">Keys left out because their type cannot be written (streams).</param>
public sealed record RdbWriteResult(int WrittenKeys, int SkippedKeys);
