using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Cinnabar.Storage.Persistence;

/// <summary>
/// Writes keys to an RDB snapshot (format version 9) that <see cref="RdbReader"/> can load.
/// Strings are written as type 0, and lists, sets, sorted sets and hashes in their plain
/// encodings: types 1, 2, 5 (scores as binary doubles) and 4, which Redis can load too.
/// </summary>
/// <remarks>
/// Streams, with their consumer groups, use Cinnabar's own encoding
/// (<see cref="RdbTypes.CinnabarStream"/>), so a file that contains a stream loads in Cinnabar
/// but not in Redis.
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
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

                // Force the data to disk before the rename, or a crash can leave the new
                // name pointing at an empty file after the old snapshot was replaced.
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Best effort; the next save overwrites it.
            }

            throw;
        }
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
            if (key.Type is KeyType.None)
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

            switch (key.Type)
            {
                case KeyType.String:
                    WriteByte(writer, RdbTypes.String);
                    WriteString(writer, key.Key);
                    WriteString(writer, (string)key.Value);
                    break;

                case KeyType.List or KeyType.Set:
                {
                    var items = (IReadOnlyCollection<string>)key.Value;
                    WriteByte(writer, key.Type == KeyType.List ? RdbTypes.List : RdbTypes.Set);
                    WriteString(writer, key.Key);
                    WriteLength(writer, items.Count);
                    foreach (string item in items)
                    {
                        WriteString(writer, item);
                    }

                    break;
                }

                case KeyType.Hash:
                {
                    var fields = (IReadOnlyCollection<KeyValuePair<string, string>>)key.Value;
                    WriteByte(writer, RdbTypes.Hash);
                    WriteString(writer, key.Key);
                    WriteLength(writer, fields.Count);
                    foreach ((string field, string value) in fields)
                    {
                        WriteString(writer, field);
                        WriteString(writer, value);
                    }

                    break;
                }

                case KeyType.SortedSet:
                {
                    var members = (IReadOnlyCollection<ScoredMember>)key.Value;
                    WriteByte(writer, RdbTypes.SortedSet2);
                    WriteString(writer, key.Key);
                    WriteLength(writer, members.Count);
                    foreach ((string member, double score) in members)
                    {
                        WriteString(writer, member);
                        Span<byte> span = writer.GetSpan(8);
                        BinaryPrimitives.WriteDoubleLittleEndian(span, score);
                        writer.Advance(8);
                    }

                    break;
                }

                case KeyType.Stream:
                    WriteByte(writer, RdbTypes.CinnabarStream);
                    WriteString(writer, key.Key);
                    WriteStream(writer, StreamSnapshot.From(key.Value));
                    break;
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

    /// <summary>
    /// Writes a stream in the <see cref="RdbTypes.CinnabarStream"/> layout: a version byte, the
    /// entries (ID, then field/value pairs), then each group's name, last delivered ID,
    /// consumers (name, seen time) and pending entries (ID, owner, delivery time and count).
    /// </summary>
    private static void WriteStream(ArrayBufferWriter<byte> writer, StreamSnapshot stream)
    {
        WriteByte(writer, RdbTypes.CinnabarStreamVersion);

        WriteLength(writer, stream.Entries.Count);
        foreach (StreamEntry entry in stream.Entries)
        {
            WriteId(writer, entry.Id);
            WriteLength(writer, entry.Fields.Count);
            foreach ((string field, string value) in entry.Fields)
            {
                WriteString(writer, field);
                WriteString(writer, value);
            }
        }

        WriteLength(writer, stream.Groups.Count);
        foreach (StreamGroupSnapshot group in stream.Groups)
        {
            WriteString(writer, group.Name);
            WriteId(writer, group.LastDeliveredId);

            WriteLength(writer, group.Consumers.Count);
            foreach (StreamConsumerSnapshot consumer in group.Consumers)
            {
                WriteString(writer, consumer.Name);
                WriteInt64(writer, consumer.SeenTimeMs);
            }

            WriteLength(writer, group.Pending.Count);
            foreach (StreamPendingSnapshot pending in group.Pending)
            {
                WriteId(writer, pending.Id);
                WriteString(writer, pending.Consumer);
                WriteInt64(writer, pending.DeliveryTimeMs);
                WriteInt64(writer, pending.DeliveryCount);
            }
        }
    }

    private static void WriteId(ArrayBufferWriter<byte> writer, StreamId id)
    {
        Span<byte> span = writer.GetSpan(16);
        BinaryPrimitives.WriteUInt64LittleEndian(span, id.Milliseconds);
        BinaryPrimitives.WriteUInt64LittleEndian(span[8..], id.Sequence);
        writer.Advance(16);
    }

    private static void WriteInt64(ArrayBufferWriter<byte> writer, long value)
    {
        Span<byte> span = writer.GetSpan(8);
        BinaryPrimitives.WriteInt64LittleEndian(span, value);
        writer.Advance(8);
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
        byte[] bytes = ByteString.Encoding.GetBytes(value);
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
