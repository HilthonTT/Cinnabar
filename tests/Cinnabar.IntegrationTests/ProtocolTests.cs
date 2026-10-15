using System.Text;
using Cinnabar.Protocol;

namespace Cinnabar.IntegrationTests;

/// <summary>Wire-level behaviour over a raw socket: pipelining, framing, inline commands, malformed input.</summary>
public sealed class ProtocolTests : IAsyncLifetime
{
    private TestServer _server = null!;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task PipelinedCommands_InOneWrite_GetRepliesInOrder()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        byte[] batch =
        [
            .. RespEncoder.Command("SET", "k", "1"),
            .. RespEncoder.Command("INCR", "k"),
            .. RespEncoder.Command("GET", "k"),
            .. RespEncoder.Command("ECHO", "done"),
            .. RespEncoder.Command("PING"),
        ];
        await connection.WriteAsync(batch);

        (await connection.ReadAsync()).AsString().Should().Be("OK");
        (await connection.ReadAsync()).AsLong().Should().Be(2);
        (await connection.ReadAsync()).AsString().Should().Be("2");
        (await connection.ReadAsync()).AsString().Should().Be("done");
        (await connection.ReadAsync()).AsString().Should().Be("PONG");
    }

    [Fact]
    public async Task ManyPipelinedCommands_AllAnswered()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        const int count = 500;
        var batch = new List<byte>();
        for (int i = 0; i < count; i++)
        {
            batch.AddRange(RespEncoder.Command("INCR", "n"));
        }

        await connection.WriteAsync([.. batch]);

        for (int i = 1; i <= count; i++)
        {
            (await connection.ReadAsync()).AsLong().Should().Be(i);
        }
    }

    [Fact]
    public async Task CommandSplitAcrossWrites_IsReassembled()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        // The delays force the pieces into separate TCP segments.
        await connection.WriteAsync("*3\r\n$3\r\nSE");
        await Task.Delay(50);
        await connection.WriteAsync("T\r\n$3\r\nke");
        await Task.Delay(50);
        await connection.WriteAsync("y\r\n$5\r\nval");
        await Task.Delay(50);
        await connection.WriteAsync("ue\r\n");

        (await connection.ReadAsync()).AsString().Should().Be("OK");
        (await connection.CommandAsync("GET", "key")).AsString().Should().Be("value");
    }

    [Fact]
    public async Task InlineCommands_Work()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        await connection.WriteAsync("PING\r\n");
        RespValue pong = await connection.ReadAsync();
        pong.Type.Should().Be(RespType.SimpleString);
        pong.Text.Should().Be("PONG");

        await connection.WriteAsync("SET a b\r\n");
        (await connection.ReadAsync()).AsString().Should().Be("OK");

        await connection.WriteAsync("GET a\r\n");
        (await connection.ReadAsync()).AsString().Should().Be("b");
    }

    [Fact]
    public async Task MalformedBulkLength_GetsProtocolErrorAndIsClosed()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        await connection.WriteAsync("*1\r\n$abc\r\n");

        RespValue error = await connection.ReadAsync();
        error.IsError.Should().BeTrue();
        error.Text.Should().StartWith("ERR Protocol error");
        (await connection.IsClosedByServerAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task HugeBulkLength_IsRejectedAndClosed()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        // 600 MB announced: over Redis' 512 MB limit. The server must not try to buffer it.
        await connection.WriteAsync("*2\r\n$3\r\nGET\r\n$600000000\r\n");

        RespValue error = await connection.ReadAsync();
        error.IsError.Should().BeTrue();
        error.Text.Should().StartWith("ERR Protocol error");
        (await connection.IsClosedByServerAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task MalformedInput_DoesNotAffectOtherConnections()
    {
        await using RawConnection bad = await RawConnection.ConnectAsync(_server.Port);
        await bad.WriteAsync("*1\r\n$abc\r\n");
        (await bad.ReadAsync()).IsError.Should().BeTrue();

        (await _server.Client.PingAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task UnicodeValue_RoundTripsByteExact()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);
        const string value = "héllo wörld ✓ 😀 日本語";
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        valueBytes.Length.Should().BeGreaterThan(value.Length, "the test needs multi-byte characters");

        // Hand-built frame with the length in bytes, not characters.
        byte[] set =
        [
            .. "*3\r\n$3\r\nSET\r\n$3\r\nkey\r\n"u8.ToArray(),
            .. Encoding.ASCII.GetBytes($"${valueBytes.Length}\r\n"),
            .. valueBytes,
            .. "\r\n"u8.ToArray(),
        ];
        await connection.WriteAsync(set);
        (await connection.ReadAsync()).AsString().Should().Be("OK");

        await connection.SendAsync("GET", "key");
        byte[] expected = [.. Encoding.ASCII.GetBytes($"${valueBytes.Length}\r\n"), .. valueBytes, .. "\r\n"u8.ToArray()];
        (await connection.ReadBytesAsync(expected.Length)).Should().Equal(expected);

        (await _server.Client.GetAsync("key")).Should().Be(value);
    }

    [Fact]
    public async Task BinarySafeKeysAndValues_WithCrLfInside()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        (await connection.CommandAsync("SET", "line\r\nbreak", "a\r\nb\0c")).AsString().Should().Be("OK");
        (await connection.CommandAsync("GET", "line\r\nbreak")).AsString().Should().Be("a\r\nb\0c");
    }

    [Fact]
    public async Task NonArrayRespValue_GetsErrorButConnectionStaysOpen()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        await connection.WriteAsync(":42\r\n");
        (await connection.ReadAsync()).IsError.Should().BeTrue();

        (await connection.CommandAsync("PING")).AsString().Should().Be("PONG");
    }
}
