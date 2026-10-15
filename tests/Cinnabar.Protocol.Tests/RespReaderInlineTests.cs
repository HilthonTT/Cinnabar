namespace Cinnabar.Protocol.Tests;

public class RespReaderInlineTests
{
    private static readonly RespReaderOptions Inline = new() { AllowInlineCommands = true };

    [Theory]
    [InlineData("PING\r\n", new[] { "PING" })]
    [InlineData("SET a b\r\n", new[] { "SET", "a", "b" })]
    [InlineData("  SET   a\t b  \r\n", new[] { "SET", "a", "b" })]
    [InlineData("echo héllo\r\n", new[] { "echo", "héllo" })]
    public void Inline_command_becomes_array_of_bulk_strings(string wire, string[] expected)
    {
        RespValue value = Resp.ParseSingle(wire, Inline);

        value.Type.Should().Be(RespType.Array);
        value.AsArray().Should().AllSatisfy(v => v.Type.Should().Be(RespType.BulkString));
        value.AsStringList().Should().Equal(expected);
    }

    [Fact]
    public void Blank_inline_line_yields_empty_array()
    {
        RespValue value = Resp.ParseSingle("   \r\n", Inline);

        value.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void Server_options_allow_inline_commands()
    {
        RespReaderOptions.Server.AllowInlineCommands.Should().BeTrue();
        RespReaderOptions.Default.AllowInlineCommands.Should().BeFalse();

        Resp.ParseSingle("PING\r\n", RespReaderOptions.Server).AsStringList().Should().Equal("PING");
    }

    [Fact]
    public void Incomplete_inline_command_needs_more_data()
    {
        var buffer = Resp.Sequence("SET a b");

        RespReader.TryRead(ref buffer, out _, Inline).Should().BeFalse();
        buffer.Length.Should().Be(7);
    }

    [Fact]
    public void Inline_command_followed_by_resp_command()
    {
        var buffer = Resp.Sequence("PING\r\n*1\r\n$4\r\nPING\r\n");

        RespReader.TryRead(ref buffer, out RespValue? first, Inline).Should().BeTrue();
        RespReader.TryRead(ref buffer, out RespValue? second, Inline).Should().BeTrue();

        first!.AsStringList().Should().Equal("PING");
        second!.AsStringList().Should().Equal("PING");
    }

    [Theory]
    [InlineData("PING\r\n")]
    [InlineData("SET a b\r\n")]
    [InlineData("?\r\n")]
    public void Inline_command_is_rejected_by_default(string wire)
    {
        var buffer = Resp.Sequence(wire);

        Action act = () => RespReader.TryRead(ref buffer, out _);

        act.Should().Throw<RespProtocolException>();
    }

    [Fact]
    public void Inline_syntax_is_not_accepted_inside_an_array()
    {
        var buffer = Resp.Sequence("*1\r\nPING\r\n");

        Action act = () => RespReader.TryRead(ref buffer, out _, Inline);

        act.Should().Throw<RespProtocolException>();
    }
}
