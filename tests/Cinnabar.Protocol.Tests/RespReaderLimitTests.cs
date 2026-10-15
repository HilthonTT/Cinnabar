namespace Cinnabar.Protocol.Tests;

/// <summary>Malformed input and configured limits.</summary>
public class RespReaderLimitTests
{
    private static Action Parsing(string wire, RespReaderOptions? options = null) => () =>
    {
        var buffer = Resp.Sequence(wire);
        RespReader.TryRead(ref buffer, out _, options);
    };

    [Fact]
    public void Bulk_string_at_max_length_is_accepted()
    {
        var options = new RespReaderOptions { MaxBulkLength = 10 };

        Resp.ParseSingle("$10\r\n0123456789\r\n", options).Text.Should().Be("0123456789");
    }

    [Fact]
    public void Bulk_string_over_max_length_is_rejected_before_payload_arrives()
    {
        var options = new RespReaderOptions { MaxBulkLength = 10 };

        Parsing("$11\r\n", options).Should().Throw<RespProtocolException>().WithMessage("*11*");
    }

    [Fact]
    public void Array_at_max_length_is_accepted()
    {
        var options = new RespReaderOptions { MaxArrayLength = 2 };

        Resp.ParseSingle("*2\r\n:1\r\n:2\r\n", options).AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void Array_over_max_length_is_rejected_before_elements_arrive()
    {
        var options = new RespReaderOptions { MaxArrayLength = 2 };

        Parsing("*3\r\n", options).Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Line_at_max_length_is_accepted()
    {
        var options = new RespReaderOptions { MaxLineLength = 8 };

        // The line includes the type byte: "+abcdefg" is 8 bytes.
        Resp.ParseSingle("+abcdefg\r\n", options).Text.Should().Be("abcdefg");
    }

    [Fact]
    public void Terminated_line_over_max_length_is_rejected()
    {
        var options = new RespReaderOptions { MaxLineLength = 8 };

        Parsing("+abcdefgh\r\n", options).Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Unterminated_line_over_max_length_is_rejected()
    {
        var options = new RespReaderOptions { MaxLineLength = 8 };

        Parsing("+" + new string('a', 20), options).Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Unterminated_header_over_max_length_is_rejected()
    {
        var options = new RespReaderOptions { MaxLineLength = 8 };

        Parsing("$1234567890", options).Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Unterminated_header_within_max_length_needs_more_data()
    {
        var options = new RespReaderOptions { MaxLineLength = 8 };
        var buffer = Resp.Sequence("$123");

        RespReader.TryRead(ref buffer, out _, options).Should().BeFalse();
    }

    [Fact]
    public void Unterminated_inline_command_over_max_length_is_rejected()
    {
        var options = new RespReaderOptions { MaxLineLength = 8, AllowInlineCommands = true };

        Parsing("SET key value", options).Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Nesting_at_max_depth_is_accepted()
    {
        var options = new RespReaderOptions { MaxDepth = 2 };

        Resp.ParseSingle("*1\r\n*1\r\n:1\r\n", options).ToString().Should().Be("[[:1]]");
    }

    [Fact]
    public void Nesting_over_max_depth_is_rejected()
    {
        var options = new RespReaderOptions { MaxDepth = 2 };

        Parsing("*1\r\n*1\r\n*0\r\n", options).Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Default_max_depth_rejects_deeply_nested_arrays()
    {
        string wire = string.Concat(Enumerable.Repeat("*1\r\n", 100)) + ":1\r\n";

        Parsing(wire).Should().Throw<RespProtocolException>();
    }

    [Theory]
    [InlineData(":abc\r\n")]
    [InlineData(":12a\r\n")]
    [InlineData(":\r\n")]
    [InlineData(": 1\r\n")]
    [InlineData(":1.5\r\n")]
    [InlineData(":9223372036854775808\r\n")]
    [InlineData("$x\r\n")]
    [InlineData("$\r\n")]
    [InlineData("*two\r\n")]
    [InlineData("*1.5\r\n")]
    public void Bad_integer_is_rejected(string wire)
    {
        Parsing(wire).Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Overlong_integer_header_is_rejected()
    {
        Parsing(":" + new string('1', 30) + "\r\n").Should().Throw<RespProtocolException>();
    }

    [Theory]
    [InlineData("$3\r\nabcXY")]
    [InlineData("$3\r\nabcd\r\n")]
    [InlineData("$3\r\nab\r\n\r\n")]
    public void Bulk_string_not_terminated_by_crlf_is_rejected(string wire)
    {
        Parsing(wire).Should().Throw<RespProtocolException>();
    }

    [Theory]
    [InlineData("?foo\r\n")]
    [InlineData("%1\r\n")]
    [InlineData("\r\n")]
    public void Unknown_type_byte_is_rejected(string wire)
    {
        Parsing(wire).Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Unknown_type_byte_inside_array_is_rejected()
    {
        Parsing("*1\r\n?x\r\n").Should().Throw<RespProtocolException>();
    }
}
