using System.Text;

namespace Cinnabar.Protocol.Tests;

public class RespEncoderTests
{
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static string Text(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void Constants_have_expected_wire_form()
    {
        Text(RespEncoder.Ok).Should().Be("+OK\r\n");
        Text(RespEncoder.NullBulkString).Should().Be("$-1\r\n");
        Text(RespEncoder.NullArray).Should().Be("*-1\r\n");
        Text(RespEncoder.EmptyArray).Should().Be("*0\r\n");
    }

    [Fact]
    public void SimpleString_encodes_text()
    {
        Text(RespEncoder.SimpleString("PONG")).Should().Be("+PONG\r\n");
    }

    [Fact]
    public void SimpleString_replaces_cr_and_lf_with_spaces()
    {
        Text(RespEncoder.SimpleString("a\r\nb\nc\rd")).Should().Be("+a  b c d\r\n");
    }

    [Fact]
    public void Error_encodes_message()
    {
        Text(RespEncoder.Error("ERR syntax error")).Should().Be("-ERR syntax error\r\n");
    }

    [Fact]
    public void Error_replaces_cr_and_lf_with_spaces()
    {
        Text(RespEncoder.Error("ERR line1\r\nline2")).Should().Be("-ERR line1  line2\r\n");
    }

    [Theory]
    [InlineData(0L, ":0\r\n")]
    [InlineData(42L, ":42\r\n")]
    [InlineData(-7L, ":-7\r\n")]
    [InlineData(long.MaxValue, ":9223372036854775807\r\n")]
    [InlineData(long.MinValue, ":-9223372036854775808\r\n")]
    public void Integer_encodes_decimal(long value, string expected)
    {
        Text(RespEncoder.Integer(value)).Should().Be(expected);
    }

    [Fact]
    public void BulkString_encodes_ascii()
    {
        Text(RespEncoder.BulkString("hello")).Should().Be("$5\r\nhello\r\n");
    }

    [Fact]
    public void BulkString_encodes_empty_string()
    {
        Text(RespEncoder.BulkString("")).Should().Be("$0\r\n\r\n");
    }

    [Theory]
    [InlineData("€", 3)]
    [InlineData("日本語", 9)]
    [InlineData("héllo", 6)]
    [InlineData("🎉", 4)]
    public void BulkString_length_is_utf8_byte_count(string value, int byteCount)
    {
        Text(RespEncoder.BulkString(value)).Should().Be($"${byteCount}\r\n{value}\r\n");
    }

    [Fact]
    public void BulkString_null_is_null_bulk_string()
    {
        Text(RespEncoder.BulkString((string?)null)).Should().Be("$-1\r\n");
    }

    [Fact]
    public void BulkString_keeps_crlf_inside_payload()
    {
        Text(RespEncoder.BulkString("a\r\nb")).Should().Be("$4\r\na\r\nb\r\n");
    }

    [Fact]
    public void BulkString_from_raw_bytes()
    {
        byte[] raw = [0x00, 0xFF, 0x0D, 0x0A];

        byte[] encoded = RespEncoder.BulkString(raw.AsSpan());

        encoded.Should().Equal([(byte)'$', (byte)'4', 13, 10, 0x00, 0xFF, 0x0D, 0x0A, 13, 10]);
    }

    [Fact]
    public void BulkStringArray_encodes_each_element()
    {
        Text(RespEncoder.BulkStringArray(["a", "bc"])).Should().Be("*2\r\n$1\r\na\r\n$2\r\nbc\r\n");
    }

    [Fact]
    public void BulkStringArray_encodes_null_elements_as_null_bulk()
    {
        Text(RespEncoder.BulkStringArray(["a", null, "c"])).Should().Be("*3\r\n$1\r\na\r\n$-1\r\n$1\r\nc\r\n");
    }

    [Fact]
    public void BulkStringArray_of_empty_sequence_is_empty_array()
    {
        Text(RespEncoder.BulkStringArray([])).Should().Be("*0\r\n");
    }

    [Fact]
    public void BulkStringArray_accepts_lazy_enumerable()
    {
        IEnumerable<string?> lazy = Enumerable.Range(1, 3).Select(i => (string?)i.ToString());

        Text(RespEncoder.BulkStringArray(lazy)).Should().Be("*3\r\n$1\r\n1\r\n$1\r\n2\r\n$1\r\n3\r\n");
    }

    [Fact]
    public void Array_concatenates_already_encoded_parts()
    {
        byte[] encoded = RespEncoder.Array([RespEncoder.Integer(1), RespEncoder.SimpleString("x"), RespEncoder.NullArray.ToArray()]);

        Text(encoded).Should().Be("*3\r\n:1\r\n+x\r\n*-1\r\n");
    }

    [Fact]
    public void Array_of_nothing_is_empty_array()
    {
        Text(RespEncoder.Array([])).Should().Be("*0\r\n");
    }

    [Fact]
    public void Command_is_array_of_bulk_strings()
    {
        Text(RespEncoder.Command("SET", "key", "välue")).Should().Be("*3\r\n$3\r\nSET\r\n$3\r\nkey\r\n$6\r\nvälue\r\n");
    }

    [Fact]
    public void Command_accepts_list()
    {
        Text(RespEncoder.Command(new List<string> { "PING" })).Should().Be("*1\r\n$4\r\nPING\r\n");
    }

    [Fact]
    public void Encode_writes_null_types()
    {
        Text(RespEncoder.Encode(RespValue.Null())).Should().Be("$-1\r\n");
        Text(RespEncoder.Encode(RespValue.NullArray())).Should().Be("*-1\r\n");
    }

    public static TheoryData<string> RoundTripWires => new()
    {
        "+OK\r\n",
        "-ERR bad thing\r\n",
        ":-42\r\n",
        "$5\r\nhello\r\n",
        "$0\r\n\r\n",
        "$9\r\n日本語\r\n",
        "$-1\r\n",
        "*-1\r\n",
        "*0\r\n",
        "*4\r\n+a\r\n:1\r\n$-1\r\n*2\r\n$1\r\nx\r\n*-1\r\n",
    };

    [Theory]
    [MemberData(nameof(RoundTripWires))]
    public void Encode_round_trips_through_reader(string wire)
    {
        RespValue parsed = Resp.ParseSingle(wire);

        byte[] encoded = RespEncoder.Encode(parsed);

        Text(encoded).Should().Be(wire);
        Resp.ParseSingle(encoded).ToString().Should().Be(parsed.ToString());
    }

    [Fact]
    public void Encode_of_constructed_value_round_trips()
    {
        RespValue value = RespValue.Array([
            RespValue.BulkString("€uro"),
            RespValue.Integer(long.MinValue),
            RespValue.Error("WRONGTYPE no"),
            RespValue.Array([]),
        ]);

        RespValue parsed = Resp.ParseSingle(RespEncoder.Encode(value));

        parsed.ToString().Should().Be(value.ToString());
        parsed.AsArray().Select(v => v.Type).Should().Equal(value.AsArray().Select(v => v.Type));
    }

    [Fact]
    public void Each_call_returns_a_new_array()
    {
        RespEncoder.SimpleString("x").Should().NotBeSameAs(RespEncoder.SimpleString("x"));
    }
}
