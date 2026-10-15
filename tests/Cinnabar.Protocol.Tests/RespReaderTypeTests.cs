using System.Buffers;

namespace Cinnabar.Protocol.Tests;

public class RespReaderTypeTests
{
    [Fact]
    public void Reads_simple_string()
    {
        RespValue value = Resp.ParseSingle("+OK\r\n");

        value.Type.Should().Be(RespType.SimpleString);
        value.Text.Should().Be("OK");
    }

    [Fact]
    public void Reads_empty_simple_string()
    {
        RespValue value = Resp.ParseSingle("+\r\n");

        value.Type.Should().Be(RespType.SimpleString);
        value.Text.Should().BeEmpty();
    }

    [Fact]
    public void Reads_error()
    {
        RespValue value = Resp.ParseSingle("-ERR unknown command\r\n");

        value.Type.Should().Be(RespType.Error);
        value.IsError.Should().BeTrue();
        value.Text.Should().Be("ERR unknown command");
    }

    [Theory]
    [InlineData(":0\r\n", 0L)]
    [InlineData(":42\r\n", 42L)]
    [InlineData(":-17\r\n", -17L)]
    [InlineData(":9223372036854775807\r\n", long.MaxValue)]
    [InlineData(":-9223372036854775808\r\n", long.MinValue)]
    public void Reads_integer(string wire, long expected)
    {
        RespValue value = Resp.ParseSingle(wire);

        value.Type.Should().Be(RespType.Integer);
        value.IntegerValue.Should().Be(expected);
    }

    [Fact]
    public void Reads_bulk_string()
    {
        RespValue value = Resp.ParseSingle("$5\r\nhello\r\n");

        value.Type.Should().Be(RespType.BulkString);
        value.Text.Should().Be("hello");
    }

    [Fact]
    public void Reads_empty_bulk_string()
    {
        RespValue value = Resp.ParseSingle("$0\r\n\r\n");

        value.Type.Should().Be(RespType.BulkString);
        value.Text.Should().BeEmpty();
    }

    [Fact]
    public void Bulk_string_may_contain_crlf()
    {
        RespValue value = Resp.ParseSingle("$4\r\na\r\nb\r\n");

        value.Text.Should().Be("a\r\nb");
    }

    [Fact]
    public void Reads_null_bulk_string()
    {
        RespValue value = Resp.ParseSingle("$-1\r\n");

        value.Type.Should().Be(RespType.Null);
        value.IsNull.Should().BeTrue();
    }

    [Fact]
    public void Reads_null_array()
    {
        RespValue value = Resp.ParseSingle("*-1\r\n");

        value.Type.Should().Be(RespType.NullArray);
        value.IsNull.Should().BeTrue();
    }

    [Fact]
    public void Reads_empty_array()
    {
        RespValue value = Resp.ParseSingle("*0\r\n");

        value.Type.Should().Be(RespType.Array);
        value.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void Reads_array_of_mixed_types()
    {
        RespValue value = Resp.ParseSingle("*5\r\n+OK\r\n-ERR x\r\n:7\r\n$3\r\nfoo\r\n$-1\r\n");

        value.Type.Should().Be(RespType.Array);
        value.AsArray().Select(v => v.Type).Should().Equal(
            RespType.SimpleString, RespType.Error, RespType.Integer, RespType.BulkString, RespType.Null);
        value.ToString().Should().Be("[+OK, -ERR x, :7, \"foo\", (nil)]");
    }

    [Fact]
    public void Reads_nested_arrays()
    {
        RespValue value = Resp.ParseSingle("*2\r\n*2\r\n:1\r\n:2\r\n*1\r\n*0\r\n");

        value.ToString().Should().Be("[[:1, :2], [[]]]");
    }

    [Fact]
    public void Array_may_contain_null_array()
    {
        RespValue value = Resp.ParseSingle("*1\r\n*-1\r\n");

        value.AsArray()[0].Type.Should().Be(RespType.NullArray);
    }

    [Theory]
    [InlineData("€", 3)]
    [InlineData("日本", 6)]
    [InlineData("héllo", 6)]
    [InlineData("🎉", 4)]
    public void Bulk_string_length_counts_utf8_bytes(string text, int byteLength)
    {
        RespValue value = Resp.ParseSingle($"${byteLength}\r\n{text}\r\n");

        value.Text.Should().Be(text);
    }

    [Fact]
    public void Utf8_bulk_string_with_char_count_as_length_is_rejected()
    {
        // "日本" is 2 chars but 6 bytes; a length of 2 leaves bytes where CRLF should be.
        var buffer = Resp.Sequence("$2\r\n日本\r\n");

        Action act = () => RespReader.TryRead(ref buffer, out _);

        act.Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Reads_pipelined_values_one_at_a_time()
    {
        var buffer = Resp.Sequence("+OK\r\n:1\r\n$3\r\nfoo\r\n*1\r\n:2\r\n");
        var values = new List<string>();

        while (RespReader.TryRead(ref buffer, out RespValue? value))
        {
            values.Add(value!.ToString());
        }

        values.Should().Equal("+OK", ":1", "\"foo\"", "[:2]");
        buffer.Length.Should().Be(0);
    }

    [Fact]
    public void Leaves_following_bytes_in_buffer()
    {
        var buffer = Resp.Sequence("+OK\r\n+NEXT");

        RespReader.TryRead(ref buffer, out RespValue? value).Should().BeTrue();

        value!.Text.Should().Be("OK");
        Resp.Bytes("+NEXT").Should().Equal(buffer.ToArray());
    }

    [Fact]
    public void Empty_buffer_needs_more_data()
    {
        var buffer = ReadOnlySequence<byte>.Empty;

        RespReader.TryRead(ref buffer, out RespValue? value).Should().BeFalse();
        value.Should().BeNull();
    }
}
