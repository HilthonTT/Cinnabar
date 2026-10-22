using System.Text;

namespace Cinnabar.Protocol.Tests;

/// <summary>The byte-per-char text encoding the server uses to keep payloads binary-safe.</summary>
public class RespByteExactTests
{
    [Fact]
    public void Server_and_byte_exact_options_decode_one_char_per_byte()
    {
        RespReaderOptions.Server.TextEncoding.Should().Be(Encoding.Latin1);
        RespReaderOptions.ByteExact.TextEncoding.Should().Be(Encoding.Latin1);
        RespReaderOptions.Default.TextEncoding.Should().Be(Encoding.UTF8);
    }

    [Fact]
    public void Every_byte_value_round_trips()
    {
        byte[] payload = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
        byte[] wire = RespEncoder.BulkString(payload);

        RespValue value = Resp.ParseSingle(wire, RespReaderOptions.ByteExact);

        value.Text!.Length.Should().Be(256);
        RespEncoder.Encode(value, Encoding.Latin1).Should().Equal(wire);
    }

    [Fact]
    public void Invalid_utf8_is_preserved()
    {
        byte[] wire = [.. "$3\r\n"u8.ToArray(), 0xC3, 0x28, 0xFF, .. "\r\n"u8.ToArray()];

        RespValue value = Resp.ParseSingle(wire, RespReaderOptions.ByteExact);

        value.Text.Should().Be("Ã(ÿ");
        RespEncoder.BulkString(value.Text, Encoding.Latin1).Should().Equal(wire);
    }

    [Fact]
    public void Byte_exact_decoding_works_across_segments()
    {
        byte[] wire = [.. "$3\r\n"u8.ToArray(), 0xE2, 0x9C, 0x93, .. "\r\n"u8.ToArray()];
        var buffer = Resp.OneBytePerSegment(wire);

        RespReader.TryRead(ref buffer, out RespValue? value, RespReaderOptions.ByteExact).Should().BeTrue();

        value!.Text.Should().Be("â\u009c\u0093");
    }

    [Fact]
    public void Utf8_text_arrives_as_its_bytes()
    {
        RespValue value = Resp.ParseSingle(Resp.Bytes("$2\r\né\r\n"), RespReaderOptions.ByteExact);

        value.Text.Should().Be("Ã©");
    }

    [Fact]
    public void Inline_commands_do_not_split_inside_multibyte_characters()
    {
        // "à" is C3 A0; decoded byte-for-byte, A0 is a no-break space that must not split.
        RespValue value = Resp.ParseSingle(Resp.Bytes("ECHO à\r\n"), RespReaderOptions.Server);

        value.AsStringList().Should().Equal("ECHO", "Ã ");
    }

    [Fact]
    public void Command_can_be_encoded_byte_exact()
    {
        RespEncoder.Command(["SET", "k", "ÿ"], Encoding.Latin1)
            .Should().Equal([.. "*3\r\n$3\r\nSET\r\n$1\r\nk\r\n$1\r\n"u8.ToArray(), 0xFF, 13, 10]);
    }

    [Fact]
    public void Simple_strings_and_errors_accept_an_encoding()
    {
        RespEncoder.SimpleString("é", Encoding.Latin1).Should().Equal([(byte)'+', 0xE9, 13, 10]);
        RespEncoder.Error("ERR é", Encoding.Latin1).Should().Equal([.. "-ERR "u8.ToArray(), 0xE9, 13, 10]);
    }
}
