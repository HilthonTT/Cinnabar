using System.Text;
using Cinnabar.Server.Commands;
using Cinnabar.Storage;

namespace Cinnabar.Server.Tests.Commands;

public sealed class ReplyTests
{
    [Fact]
    public void Constants_have_expected_wire_format()
    {
        Reply.Ok.ToString().Should().Be("+OK\r\n");
        Reply.Queued.ToString().Should().Be("+QUEUED\r\n");
        Reply.NullBulkString.ToString().Should().Be("$-1\r\n");
        Reply.NullArray.ToString().Should().Be("*-1\r\n");
        Reply.EmptyArray.ToString().Should().Be("*0\r\n");
        Reply.Zero.ToString().Should().Be(":0\r\n");
        Reply.One.ToString().Should().Be(":1\r\n");
    }

    [Fact]
    public void None_is_empty()
    {
        Reply.None.IsEmpty.Should().BeTrue();
        Reply.None.ToString().Should().BeEmpty();
        Reply.Ok.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Error_is_flagged_and_encoded()
    {
        Reply reply = Reply.Error("ERR bad");

        reply.IsError.Should().BeTrue();
        reply.ToString().Should().Be("-ERR bad\r\n");
    }

    [Fact]
    public void Error_with_newline_stays_on_one_line()
    {
        Reply.Error("ERR a\r\nb").ToString().Should().NotContain("a\r\nb").And.EndWith("\r\n");
    }

    [Fact]
    public void Simple_string_is_encoded()
    {
        Reply.SimpleString("PONG").ToString().Should().Be("+PONG\r\n");
    }

    [Theory]
    [InlineData(0, ":0\r\n")]
    [InlineData(-42, ":-42\r\n")]
    [InlineData(long.MaxValue, ":9223372036854775807\r\n")]
    public void Integer_is_encoded(long value, string expected)
    {
        Reply.Integer(value).ToString().Should().Be(expected);
    }

    [Fact]
    public void Bulk_string_writes_one_byte_per_char()
    {
        Reply.BulkString("\u00e9\u0000\u00ff").Bytes.ToArray()
            .Should().Equal([(byte)'$', (byte)'3', 13, 10, 0xE9, 0x00, 0xFF, 13, 10]);
    }

    [Fact]
    public void Bulk_string_is_encoded_with_byte_length()
    {
        Reply.BulkString(ByteString.FromText("héllo")).ToString().Should().Be("$6\r\nhéllo\r\n");
        Reply.BulkString("").ToString().Should().Be("$0\r\n\r\n");
    }

    [Fact]
    public void Null_bulk_string_for_null_text()
    {
        Reply.BulkString(null).ToString().Should().Be("$-1\r\n");
    }

    [Fact]
    public void Bulk_string_array_supports_null_elements()
    {
        Reply.BulkStringArray(["a", null]).ToString().Should().Be("*2\r\n$1\r\na\r\n$-1\r\n");
    }

    [Fact]
    public void Array_nests_other_replies()
    {
        Reply reply = Reply.Array([Reply.Integer(1), Reply.BulkStringArray(["x"]), Reply.NullBulkString]);

        reply.ToString().Should().Be("*3\r\n:1\r\n*1\r\n$1\r\nx\r\n$-1\r\n");
        reply.IsError.Should().BeFalse();
    }

    [Fact]
    public void Sequence_concatenates_replies()
    {
        Reply.Sequence([Reply.Ok, Reply.Integer(2)]).ToString().Should().Be("+OK\r\n:2\r\n");
    }

    [Fact]
    public void Raw_keeps_bytes()
    {
        Reply.Raw(Encoding.ASCII.GetBytes("+X\r\n")).ToString().Should().Be("+X\r\n");
    }

    [Fact]
    public void WithPropagation_sets_command_and_keeps_bytes()
    {
        var command = new Command("DEL", "k");

        Reply reply = Reply.Ok.WithPropagation(command);

        reply.PropagateAs.Should().BeSameAs(command);
        reply.ToString().Should().Be("+OK\r\n");
        reply.SkipPropagation.Should().BeFalse();
    }

    [Fact]
    public void WithoutPropagation_sets_flag()
    {
        Reply reply = Reply.Zero.WithoutPropagation();

        reply.SkipPropagation.Should().BeTrue();
        reply.ToString().Should().Be(":0\r\n");
    }

    [Fact]
    public void ThenClose_sets_close_flag_and_preserves_original()
    {
        Reply reply = Reply.Ok.ThenClose();

        reply.CloseConnection.Should().BeTrue();
        Reply.Ok.CloseConnection.Should().BeFalse();
    }

    [Fact]
    public void Modifiers_keep_close_flag()
    {
        Reply reply = Reply.Ok.ThenClose().WithoutPropagation();

        reply.CloseConnection.Should().BeTrue();
        reply.SkipPropagation.Should().BeTrue();
    }

    [Fact]
    public void Error_modifiers_keep_error_flag()
    {
        Reply.Error("ERR x").WithoutPropagation().IsError.Should().BeTrue();
    }
}
