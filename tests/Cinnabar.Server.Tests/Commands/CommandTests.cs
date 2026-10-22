using System.Buffers;
using System.Text;
using Cinnabar.Protocol;
using Cinnabar.Server.Commands;
using Cinnabar.Storage;

namespace Cinnabar.Server.Tests.Commands;

public sealed class CommandTests
{
    [Fact]
    public void TryCreate_accepts_array_of_bulk_strings()
    {
        RespValue value = RespValue.Array([RespValue.BulkString("SET"), RespValue.BulkString("k"), RespValue.BulkString("v")]);

        Command.TryCreate(value, out Command? command).Should().BeTrue();

        command!.Name.Should().Be("SET");
        command.Arguments.Should().Equal("k", "v");
        command.Count.Should().Be(2);
        command[1].Should().Be("v");
    }

    [Fact]
    public void TryCreate_accepts_integers_and_simple_strings()
    {
        RespValue value = RespValue.Array([RespValue.SimpleString("EXPIRE"), RespValue.BulkString("k"), RespValue.Integer(10)]);

        Command.TryCreate(value, out Command? command).Should().BeTrue();

        command!.Arguments.Should().Equal("k", "10");
    }

    [Fact]
    public void TryCreate_accepts_command_without_arguments()
    {
        Command.TryCreate(RespValue.Array([RespValue.BulkString("PING")]), out Command? command).Should().BeTrue();

        command!.Count.Should().Be(0);
    }

    [Fact]
    public void TryCreate_rejects_empty_array()
    {
        Command.TryCreate(RespValue.Array([]), out Command? command).Should().BeFalse();
        command.Should().BeNull();
    }

    public static TheoryData<RespValue> NonArrays => new()
    {
        RespValue.BulkString("PING"),
        RespValue.SimpleString("PING"),
        RespValue.Integer(1),
        RespValue.Error("ERR x"),
        RespValue.Null(),
        RespValue.NullArray(),
    };

    [Theory]
    [MemberData(nameof(NonArrays))]
    public void TryCreate_rejects_non_arrays(RespValue value)
    {
        Command.TryCreate(value, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreate_rejects_nested_arrays()
    {
        RespValue value = RespValue.Array([RespValue.BulkString("SET"), RespValue.Array([RespValue.BulkString("x")])]);

        Command.TryCreate(value, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreate_rejects_error_elements()
    {
        RespValue value = RespValue.Array([RespValue.BulkString("GET"), RespValue.Error("ERR boom")]);

        Command.TryCreate(value, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreate_rejects_null_elements()
    {
        RespValue value = RespValue.Array([RespValue.BulkString("GET"), RespValue.Null()]);

        Command.TryCreate(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Encode_produces_array_of_bulk_strings()
    {
        Encoding.UTF8.GetString(new Command("SET", "k", "v").Encode())
            .Should().Be("*3\r\n$3\r\nSET\r\n$1\r\nk\r\n$1\r\nv\r\n");
    }

    [Fact]
    public void Encode_round_trips_through_reader()
    {
        var original = new Command("XADD", "stream", "*", "field", "line1\r\nline2", ByteString.FromText("é"), "\u00ff\u0000");
        var buffer = new ReadOnlySequence<byte>(original.Encode());

        RespReader.TryRead(ref buffer, out RespValue? value, RespReaderOptions.ByteExact).Should().BeTrue();
        Command.TryCreate(value!, out Command? parsed).Should().BeTrue();

        parsed!.Name.Should().Be(original.Name);
        parsed.Arguments.Should().Equal(original.Arguments);
        buffer.Length.Should().Be(0);
    }

    [Fact]
    public void ToString_joins_name_and_arguments()
    {
        new Command("SET", "k", "v").ToString().Should().Be("SET k v");
        new Command("PING").ToString().Should().Be("PING");
    }

    [Fact]
    public void ToString_shows_utf8_arguments_as_text()
    {
        new Command("SET", "k", ByteString.FromText("héllo")).ToString().Should().Be("SET k héllo");
    }
}
