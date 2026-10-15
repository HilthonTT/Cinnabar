using Cinnabar.Client;
using Cinnabar.Protocol;

namespace Cinnabar.IntegrationTests;

/// <summary>QUIT, CLIENT, SELECT, MULTI over a raw connection.</summary>
public sealed class ConnectionCommandTests : IAsyncLifetime
{
    private TestServer _server = null!;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Quit_RepliesOkThenCloses()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        // A command pipelined after QUIT must not be answered.
        await connection.WriteAsync([.. RespEncoder.Command("QUIT"), .. RespEncoder.Command("PING")]);

        (await connection.ReadAsync()).AsString().Should().Be("OK");
        (await connection.IsClosedByServerAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task ClientSetName_ThenGetName()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        (await connection.CommandAsync("CLIENT", "GETNAME")).IsNull.Should().BeTrue();
        (await connection.CommandAsync("CLIENT", "SETNAME", "worker-1")).AsString().Should().Be("OK");
        (await connection.CommandAsync("CLIENT", "GETNAME")).AsString().Should().Be("worker-1");
    }

    [Fact]
    public async Task ClientSetName_IsPerConnection()
    {
        await using RawConnection first = await RawConnection.ConnectAsync(_server.Port);
        await using RawConnection second = await RawConnection.ConnectAsync(_server.Port);

        await first.CommandAsync("CLIENT", "SETNAME", "first");

        (await second.CommandAsync("CLIENT", "GETNAME")).IsNull.Should().BeTrue();
    }

    [Fact]
    public async Task ClientSetName_WithSpace_IsRejected()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        (await connection.CommandAsync("CLIENT", "SETNAME", "has space")).IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ClientId_IsUniquePerConnection()
    {
        await using RawConnection first = await RawConnection.ConnectAsync(_server.Port);
        await using RawConnection second = await RawConnection.ConnectAsync(_server.Port);

        long a = (await first.CommandAsync("CLIENT", "ID")).AsLong();
        long b = (await second.CommandAsync("CLIENT", "ID")).AsLong();

        a.Should().NotBe(b);
    }

    [Fact]
    public async Task Select0_Ok_Select1_Error()
    {
        (await _server.Client.ExecuteAsync("SELECT", "0")).AsString().Should().Be("OK");

        Func<Task> act = () => _server.Client.ExecuteAsync("SELECT", "1");

        (await act.Should().ThrowAsync<RedisException>()).Which.Message.Should().Be("ERR DB index is out of range");
    }

    [Fact]
    public async Task WrongArity_IsAnError()
    {
        Func<Task> act = () => _server.Client.ExecuteAsync("GET");

        (await act.Should().ThrowAsync<RedisException>()).Which.Message.Should().Contain("wrong number of arguments");
    }

    [Fact]
    public async Task Multi_Discard_DropsQueuedCommands()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        (await connection.CommandAsync("MULTI")).AsString().Should().Be("OK");
        (await connection.CommandAsync("SET", "k", "v")).AsString().Should().Be("QUEUED");
        (await connection.CommandAsync("DISCARD")).AsString().Should().Be("OK");

        (await connection.CommandAsync("GET", "k")).IsNull.Should().BeTrue();
        (await connection.CommandAsync("EXEC")).Text.Should().Be("ERR EXEC without MULTI");
    }

    [Fact]
    public async Task Command_Count_IsPositive()
    {
        (await _server.Client.ExecuteAsync("COMMAND", "COUNT")).AsLong().Should().BePositive();
    }
}
