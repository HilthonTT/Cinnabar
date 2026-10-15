using System.Buffers.Binary;
using System.Text;
using Cinnabar.Storage.Persistence;

namespace Cinnabar.Storage.Tests.Persistence;

public class RdbChecksumTests
{
    private static byte[] SampleFile() =>
        RdbWriter.Write(
            [
                new KeySnapshot("greeting", KeyType.String, "hello world", null),
                new KeySnapshot("list", KeyType.List, new[] { "a", "b" }, DateTimeOffset.FromUnixTimeMilliseconds(1_900_000_000_000)),
            ],
            out _);

    [Fact]
    public void Reference_crc64_jones_matches_published_check_value()
    {
        // Sanity check of the test's own reference implementation.
        Crc64Jones.Compute(Encoding.ASCII.GetBytes("123456789")).Should().Be(0xe9c6d914c4b8d9caUL);
    }

    [Fact]
    public void Writer_checksum_is_redis_crc64_jones()
    {
        byte[] bytes = SampleFile();

        ulong stored = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(bytes.Length - 8));
        ulong expected = Crc64Jones.Compute(bytes.AsSpan(0, bytes.Length - 8));

        stored.Should().Be(expected, "Redis uses CRC-64/Jones over everything up to and including the EOF byte");
    }

    [Fact]
    public void Reader_accepts_file_with_redis_crc64_jones_checksum()
    {
        byte[] bytes = new RdbBytes().SelectDb(0).StringKey("k", "v").EndWithRedisChecksum();

        RdbReader.Read(bytes).Databases[0].Single().Value.Should().Be("v");
    }

    [Fact]
    public void Reader_accepts_writer_output()
    {
        Action act = () => RdbReader.Read(SampleFile());

        act.Should().NotThrow();
    }

    [Fact]
    public void Writer_output_ends_with_eof_then_eight_byte_checksum()
    {
        byte[] bytes = SampleFile();

        bytes[^9].Should().Be(0xFF);
    }

    [Fact]
    public void Corrupted_payload_fails_checksum()
    {
        byte[] bytes = SampleFile();
        int index = IndexOf(bytes, Encoding.ASCII.GetBytes("hello world"));
        bytes[index] = (byte)'j';

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<InvalidDataException>().WithMessage("*CRC*");
    }

    [Fact]
    public void Corrupted_checksum_fails()
    {
        byte[] bytes = SampleFile();
        bytes[^1] ^= 0x01;

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Stored_checksum_of_zero_is_accepted()
    {
        byte[] bytes = SampleFile();
        bytes.AsSpan(bytes.Length - 8).Clear();

        RdbReader.Read(bytes).Databases[0].Should().HaveCount(2);
    }

    [Fact]
    public void Every_truncation_is_rejected_as_invalid_data()
    {
        byte[] bytes = SampleFile();

        for (int length = 0; length < bytes.Length; length++)
        {
            byte[] truncated = bytes[..length];
            Action act = () => RdbReader.Read(truncated);

            act.Should().Throw<InvalidDataException>($"a file cut to {length} of {bytes.Length} bytes is truncated");
        }
    }

    [Fact]
    public void Old_versions_without_checksum_are_accepted()
    {
        byte[] bytes = new RdbBytes("0004").SelectDb(0).StringKey("k", "v").EndWithoutChecksum();

        RdbSnapshot snapshot = RdbReader.Read(bytes);

        snapshot.Version.Should().Be(4);
        snapshot.Databases[0].Single().Value.Should().Be("v");
    }

    private static int IndexOf(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle);
}
