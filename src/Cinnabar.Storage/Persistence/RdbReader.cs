using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Cinnabar.Storage.Persistence;

/// <summary>
/// Reads Redis RDB snapshot files.
/// </summary>
/// <remarks>
/// Supported: string values (raw, integer-encoded and LZF-compressed), the plain encodings
/// of lists, sets, sorted sets (types 3 and 5) and hashes, as written by <see cref="RdbWriter"/>,
/// second and millisecond expiry, auxiliary fields, resize hints, and LRU/LFU hints. The
/// compact encodings real Redis writes for small collections (listpacks, intsets, quicklists)
/// and streams raise <see cref="NotSupportedException"/>.
/// </remarks>
public static class RdbReader
{
    private const byte OpModuleAux = 0xF7;
    private const byte OpIdle = 0xF8;
    private const byte OpFreq = 0xF9;
    private const byte OpAux = 0xFA;
    private const byte OpResizeDb = 0xFB;
    private const byte OpExpireTimeMs = 0xFC;
    private const byte OpExpireTime = 0xFD;
    private const byte OpSelectDb = 0xFE;
    private const byte OpEof = 0xFF;


    /// <summary>Reads and validates an RDB file.</summary>
    /// <exception cref="InvalidDataException">The file is corrupt or fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The file uses a value type Cinnabar cannot load.</exception>
    public static async Task<RdbSnapshot> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Read(bytes);
    }

    /// <summary>Reads and validates RDB bytes.</summary>
    /// <exception cref="InvalidDataException">The data is corrupt or fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The data uses a value type Cinnabar cannot load.</exception>
    public static RdbSnapshot Read(ReadOnlySpan<byte> data)
    {
        var reader = new Cursor(data);

        int version = ReadHeader(ref reader);
        var snapshot = new RdbSnapshot { Version = version };

        int database = 0;
        DateTimeOffset? pendingExpiry = null;

        while (true)
        {
            byte opcode = reader.ReadByte();
            switch (opcode)
            {
                case OpEof:
                    VerifyChecksum(data, reader.Position, version);
                    return snapshot;

                case OpSelectDb:
                    database = checked((int)reader.ReadLength());
                    break;

                case OpResizeDb:
                    reader.ReadLength(); // hash table size
                    reader.ReadLength(); // expires table size
                    break;

                case OpAux:
                    string name = reader.ReadString();
                    string value = reader.ReadString();
                    snapshot.Metadata.Add(new KeyValuePair<string, string>(name, value));
                    break;

                case OpExpireTimeMs:
                    pendingExpiry = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64LittleEndian());
                    break;

                case OpExpireTime:
                    pendingExpiry = DateTimeOffset.FromUnixTimeSeconds(reader.ReadInt32LittleEndian());
                    break;

                case OpIdle:
                    reader.ReadLength();
                    break;

                case OpFreq:
                    reader.ReadByte();
                    break;

                case OpModuleAux:
                    throw new NotSupportedException("RDB files containing module data are not supported.");

                default:
                    KeySnapshot key = ReadKeyValue(ref reader, opcode, pendingExpiry);
                    pendingExpiry = null;

                    if (!snapshot.Databases.TryGetValue(database, out List<KeySnapshot>? keys))
                    {
                        keys = [];
                        snapshot.Databases[database] = keys;
                    }

                    keys.Add(key);
                    break;
            }
        }
    }

    private static int ReadHeader(ref Cursor reader)
    {
        ReadOnlySpan<byte> header = reader.ReadBytes(9);
        if (!header[..5].SequenceEqual("REDIS"u8))
        {
            throw new InvalidDataException("Not an RDB file: missing the REDIS magic string.");
        }

        string versionText = Encoding.ASCII.GetString(header[5..]);
        if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out int version))
        {
            throw new InvalidDataException($"Invalid RDB version '{versionText}'.");
        }

        return version;
    }

    private static KeySnapshot ReadKeyValue(ref Cursor reader, byte type, DateTimeOffset? expiresAt)
    {
        switch (type)
        {
            case RdbTypes.String:
            {
                string key = reader.ReadString();
                return new KeySnapshot(key, KeyType.String, reader.ReadString(), expiresAt);
            }

            case RdbTypes.List or RdbTypes.Set:
            {
                string key = reader.ReadString();
                long count = reader.ReadLength();
                var items = new List<string>((int)Math.Min(count, 1024));
                for (long i = 0; i < count; i++)
                {
                    items.Add(reader.ReadString());
                }

                return new KeySnapshot(key, type == RdbTypes.List ? KeyType.List : KeyType.Set, items, expiresAt);
            }

            case RdbTypes.Hash:
            {
                string key = reader.ReadString();
                long count = reader.ReadLength();
                var fields = new List<KeyValuePair<string, string>>((int)Math.Min(count, 1024));
                for (long i = 0; i < count; i++)
                {
                    string field = reader.ReadString();
                    fields.Add(new KeyValuePair<string, string>(field, reader.ReadString()));
                }

                return new KeySnapshot(key, KeyType.Hash, fields, expiresAt);
            }

            case RdbTypes.SortedSet or RdbTypes.SortedSet2:
            {
                string key = reader.ReadString();
                long count = reader.ReadLength();
                var members = new List<ScoredMember>((int)Math.Min(count, 1024));
                for (long i = 0; i < count; i++)
                {
                    string member = reader.ReadString();
                    double score = type == RdbTypes.SortedSet2
                        ? BinaryPrimitives.ReadDoubleLittleEndian(reader.ReadBytes(8))
                        : reader.ReadTextScore();
                    if (double.IsNaN(score))
                    {
                        throw new InvalidDataException($"Sorted set '{key}' has a NaN score.");
                    }

                    members.Add(new ScoredMember(member, score));
                }

                return new KeySnapshot(key, KeyType.SortedSet, members, expiresAt);
            }

            default:
                throw new NotSupportedException(
                    $"RDB value type {type} is not supported (Cinnabar loads strings and the plain list, set, sorted set and hash encodings).");
        }
    }

    private static void VerifyChecksum(ReadOnlySpan<byte> data, int checksumOffset, int version)
    {
        // Checksums were added in RDB version 5.
        if (version < 5)
        {
            return;
        }

        if (data.Length < checksumOffset + 8)
        {
            throw new InvalidDataException("RDB file is truncated: missing the CRC-64 checksum.");
        }

        ulong stored = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(checksumOffset, 8));

        // Redis writes 0 when checksums are disabled (rdbchecksum no).
        if (stored == 0)
        {
            return;
        }

        ulong computed = Crc64.Compute(data[..checksumOffset]);
        if (stored != computed)
        {
            throw new InvalidDataException(
                $"RDB file failed its CRC-64 check (expected 0x{computed:X16}, found 0x{stored:X16}); it may be corrupt or truncated.");
        }
    }

    /// <summary>A forward-only reader over the file bytes.</summary>
    private ref struct Cursor(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; }

        public byte ReadByte() => ReadBytes(1)[0];

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || Position + count > _data.Length)
            {
                throw new InvalidDataException("RDB file is truncated.");
            }

            ReadOnlySpan<byte> slice = _data.Slice(Position, count);
            Position += count;
            return slice;
        }

        public long ReadInt64LittleEndian() => BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(8));

        public int ReadInt32LittleEndian() => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4));

        /// <summary>
        /// Reads a score in the old sorted-set encoding (type 3): a length byte, where 253, 254
        /// and 255 stand for NaN, +inf and -inf, followed by the number as text.
        /// </summary>
        public double ReadTextScore()
        {
            byte length = ReadByte();
            return length switch
            {
                253 => double.NaN,
                254 => double.PositiveInfinity,
                255 => double.NegativeInfinity,
                _ => double.Parse(ReadBytes(length), NumberStyles.Float, CultureInfo.InvariantCulture),
            };
        }

        public long ReadLength()
        {
            (long value, bool isEncoded) = ReadLengthOrEncoding();
            if (isEncoded)
            {
                throw new InvalidDataException("Expected a length but found a special string encoding.");
            }

            return value;
        }

        public string ReadString()
        {
            (long value, bool isEncoded) = ReadLengthOrEncoding();
            if (!isEncoded)
            {
                return ByteString.Encoding.GetString(ReadBytes(checked((int)value)));
            }

            return value switch
            {
                0 => ((sbyte)ReadByte()).ToString(CultureInfo.InvariantCulture),
                1 => BinaryPrimitives.ReadInt16LittleEndian(ReadBytes(2)).ToString(CultureInfo.InvariantCulture),
                2 => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4)).ToString(CultureInfo.InvariantCulture),
                3 => ReadLzfString(),
                _ => throw new InvalidDataException($"Unknown RDB string encoding {value}."),
            };
        }

        private string ReadLzfString()
        {
            int compressedLength = checked((int)ReadLength());
            int length = checked((int)ReadLength());
            return ByteString.Encoding.GetString(Lzf.Decompress(ReadBytes(compressedLength), length));
        }

        /// <summary>
        /// Reads a length. The top two bits of the first byte select the format; <c>11</c>
        /// marks a special string encoding whose kind is in the low six bits.
        /// </summary>
        private (long Value, bool IsEncoded) ReadLengthOrEncoding()
        {
            byte first = ReadByte();
            return (first >> 6) switch
            {
                0b00 => (first & 0x3F, false),
                0b01 => (((first & 0x3F) << 8) | ReadByte(), false),
                0b11 => (first & 0x3F, true),
                _ => first switch
                {
                    0x80 => (BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4)), false),
                    0x81 => (checked((long)BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(8))), false),
                    _ => throw new InvalidDataException($"Invalid RDB length prefix 0x{first:X2}."),
                },
            };
        }
    }
}
