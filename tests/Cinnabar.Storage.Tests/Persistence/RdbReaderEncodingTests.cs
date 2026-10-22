using Cinnabar.Storage.Persistence;

namespace Cinnabar.Storage.Tests.Persistence;

/// <summary>Hand-built files using encodings real Redis writes but <see cref="RdbWriter"/> does not.</summary>
public class RdbReaderEncodingTests
{
    private static KeySnapshot ReadSingle(byte[] bytes) => RdbReader.Read(bytes).Databases[0].Single();

    private static object ReadEncodedValue(params byte[] encodedValue) =>
        ReadSingle(new RdbBytes().SelectDb(0).StringKeyEncoded("k", encodedValue).EndWithZeroChecksum()).Value;

    [Theory]
    [InlineData((byte)0x00, "0")]
    [InlineData((byte)0x7B, "123")]
    [InlineData((byte)0x85, "-123")]
    [InlineData((byte)0xFF, "-1")]
    [InlineData((byte)0x80, "-128")]
    public void Int8_encoded_string(byte value, string expected)
    {
        ReadEncodedValue(0xC0, value).Should().Be(expected);
    }

    [Theory]
    [InlineData(new byte[] { 0xE8, 0x03 }, "1000")]
    [InlineData(new byte[] { 0x18, 0xFC }, "-1000")]
    [InlineData(new byte[] { 0xFF, 0x7F }, "32767")]
    public void Int16_encoded_string(byte[] littleEndian, string expected)
    {
        ReadEncodedValue([0xC1, .. littleEndian]).Should().Be(expected);
    }

    [Theory]
    [InlineData(new byte[] { 0xA0, 0x86, 0x01, 0x00 }, "100000")]
    [InlineData(new byte[] { 0x60, 0x79, 0xFE, 0xFF }, "-100000")]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }, "2147483647")]
    public void Int32_encoded_string(byte[] littleEndian, string expected)
    {
        ReadEncodedValue([0xC2, .. littleEndian]).Should().Be(expected);
    }

    [Fact]
    public void Integer_encoded_key()
    {
        byte[] bytes = new RdbBytes().SelectDb(0).Raw(0x00, 0xC0, 0x05).Str("five").EndWithZeroChecksum();

        KeySnapshot key = ReadSingle(bytes);

        key.Key.Should().Be("5");
        key.Value.Should().Be("five");
    }

    [Fact]
    public void Lzf_literal_only_string()
    {
        // ctrl byte 4 = literal run of 5 bytes.
        ReadEncodedValue(0xC3, 6, 5, 0x04, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o').Should().Be("hello");
    }

    [Fact]
    public void Lzf_string_with_back_reference()
    {
        // "abc" as a literal, then copy 6 bytes from 3 back (overlapping) → "abcabcabc".
        ReadEncodedValue(0xC3, 6, 9, 0x02, (byte)'a', (byte)'b', (byte)'c', 0x80, 0x02).Should().Be("abcabcabc");
    }

    [Fact]
    public void Lzf_string_with_long_back_reference()
    {
        // "a" literal, then a length-code-7 back reference with extension byte 10: copies 19 bytes.
        ReadEncodedValue(0xC3, 5, 20, 0x00, (byte)'a', 0xE0, 0x0A, 0x00).Should().Be(new string('a', 20));
    }

    [Fact]
    public void Lzf_with_wrong_declared_length_is_invalid()
    {
        Action act = () => ReadEncodedValue(0xC3, 6, 7, 0x04, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o');

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Lzf_back_reference_before_start_is_invalid()
    {
        Action act = () => ReadEncodedValue(0xC3, 3, 3, 0x20, 0x05, 0x00);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Unknown_string_encoding_is_invalid()
    {
        Action act = () => ReadEncodedValue(0xC4, 0x00);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Two_byte_length_string()
    {
        string value = new('z', 100);
        byte[] bytes = new RdbBytes().SelectDb(0).Raw(0x00).Str("k")
            .Raw(0x40, 100).Raw(System.Text.Encoding.ASCII.GetBytes(value)).EndWithZeroChecksum();

        ReadSingle(bytes).Value.Should().Be(value);
    }

    [Fact]
    public void Four_byte_big_endian_length_string()
    {
        string value = new('q', 70);
        byte[] bytes = new RdbBytes().SelectDb(0).Raw(0x00).Str("k")
            .Raw(0x80, 0, 0, 0, 70).Raw(System.Text.Encoding.ASCII.GetBytes(value)).EndWithZeroChecksum();

        ReadSingle(bytes).Value.Should().Be(value);
    }

    [Fact]
    public void Millisecond_expiry()
    {
        byte[] bytes = new RdbBytes().SelectDb(0).ExpireMs(1_700_000_000_123).StringKey("k", "v").EndWithZeroChecksum();

        ReadSingle(bytes).ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123));
    }

    [Fact]
    public void Second_expiry()
    {
        byte[] bytes = new RdbBytes().SelectDb(0).ExpireSeconds(1_700_000_000).StringKey("k", "v").EndWithZeroChecksum();

        ReadSingle(bytes).ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
    }

    [Fact]
    public void Resize_hint_is_skipped()
    {
        byte[] bytes = new RdbBytes().SelectDb(0).Raw(0xFB, 0x02, 0x01).StringKey("a", "1").StringKey("b", "2").EndWithZeroChecksum();

        RdbReader.Read(bytes).Databases[0].Select(k => k.Key).Should().Equal("a", "b");
    }

    [Fact]
    public void Aux_fields_become_metadata_in_order()
    {
        byte[] bytes = new RdbBytes()
            .Raw(0xFA).Str("redis-ver").Str("7.2.4")
            .Raw(0xFA).Str("redis-bits").Raw(0xC0, 64)
            .SelectDb(0).StringKey("k", "v")
            .EndWithZeroChecksum();

        RdbSnapshot snapshot = RdbReader.Read(bytes);

        snapshot.Metadata.Should().Equal(
            new KeyValuePair<string, string>("redis-ver", "7.2.4"),
            new KeyValuePair<string, string>("redis-bits", "64"));
    }

    [Fact]
    public void Idle_and_freq_hints_are_skipped()
    {
        byte[] bytes = new RdbBytes().SelectDb(0)
            .Raw(0xF8, 0x05).StringKey("a", "1")
            .Raw(0xF9, 0x07).StringKey("b", "2")
            .EndWithZeroChecksum();

        RdbReader.Read(bytes).Databases[0].Select(k => k.Key).Should().Equal("a", "b");
    }

    [Fact]
    public void Keys_are_grouped_by_database()
    {
        byte[] bytes = new RdbBytes()
            .SelectDb(0).StringKey("a", "1")
            .SelectDb(3).StringKey("b", "2").StringKey("c", "3")
            .EndWithZeroChecksum();

        RdbSnapshot snapshot = RdbReader.Read(bytes);

        snapshot.Databases[0].Select(k => k.Key).Should().Equal("a");
        snapshot.Databases[3].Select(k => k.Key).Should().Equal("b", "c");
    }

    [Fact]
    public void Plain_list()
    {
        byte[] bytes = new RdbBytes().SelectDb(0).Raw(0x01).Str("l").Raw(0x02).Str("x").Raw(0xC0, 0x07).EndWithZeroChecksum();

        KeySnapshot key = ReadSingle(bytes);

        key.Type.Should().Be(KeyType.List);
        ((IEnumerable<string>)key.Value).Should().Equal("x", "7");
    }

    [Theory]
    [InlineData((byte)11)] // intset
    [InlineData((byte)14)] // quicklist
    [InlineData((byte)16)] // hash listpack
    [InlineData((byte)17)] // sorted set listpack
    [InlineData((byte)15)] // stream
    public void Unsupported_value_type_is_not_supported(byte type)
    {
        byte[] bytes = new RdbBytes().SelectDb(0).Raw(type).Str("k").Str("v").EndWithZeroChecksum();

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Module_aux_is_not_supported()
    {
        byte[] bytes = new RdbBytes().Raw(0xF7, 0x00).EndWithZeroChecksum();

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Missing_magic_is_invalid()
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("RODIS0009\xFF");

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Non_numeric_version_is_invalid()
    {
        byte[] bytes = new RdbBytes("00x9").EndWithZeroChecksum();

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Missing_eof_is_invalid()
    {
        byte[] bytes = new RdbBytes().SelectDb(0).StringKey("k", "v").ToArray();

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Encoded_value_where_length_expected_is_invalid()
    {
        byte[] bytes = new RdbBytes().Raw(0xFE, 0xC0, 0x00).EndWithZeroChecksum();

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<InvalidDataException>();
    }
}
