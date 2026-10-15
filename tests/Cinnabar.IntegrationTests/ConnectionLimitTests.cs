using Cinnabar.Protocol;

namespace Cinnabar.IntegrationTests;

public sealed class MaxConnectionsPerIpTests : IAsyncLifetime
{
    private TestServer _server = null!;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync(s => s.Security.MaxConnectionsPerIp = 1);

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task SecondConnectionFromSameIp_IsRejectedAndClosed()
    {
        await using RawConnection first = await RawConnection.ConnectAsync(_server.Port);
        (await first.CommandAsync("PING")).AsString().Should().Be("PONG");

        await using RawConnection second = await RawConnection.ConnectAsync(_server.Port);
        RespValue error = await second.ReadAsync();

        error.IsError.Should().BeTrue();
        error.Text.Should().Be("ERR max number of clients per IP address reached");
        (await second.IsClosedByServerAsync()).Should().BeTrue();
        (await first.CommandAsync("PING")).AsString().Should().Be("PONG", "the first connection is unaffected");
    }

    [Fact]
    public async Task SlotIsReleasedWhenConnectionCloses()
    {
        RawConnection first = await RawConnection.ConnectAsync(_server.Port);
        (await first.CommandAsync("QUIT")).AsString().Should().Be("OK");
        await first.DisposeAsync();

        // The server releases the slot asynchronously after the close.
        await Wait.UntilAsync(
            async () =>
            {
                await using RawConnection next = await RawConnection.ConnectAsync(_server.Port);
                await next.SendAsync("PING");
                RespValue reply = await next.ReadAsync();
                return reply.AsString() == "PONG";
            },
            "a new connection is accepted after the old one closed");
    }
}

public sealed class RateLimitTests : IAsyncLifetime
{
    private TestServer _server = null!;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync(s =>
    {
        s.Security.RateLimitPerSecond = 1;
        s.Security.RateLimitBurst = 2;
    });

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task ThirdImmediateCommand_IsRateLimited()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);
        await connection.WriteAsync([.. RespEncoder.Command("PING"), .. RespEncoder.Command("PING"), .. RespEncoder.Command("PING")]);

        (await connection.ReadAsync()).AsString().Should().Be("PONG");
        (await connection.ReadAsync()).AsString().Should().Be("PONG");
        RespValue third = await connection.ReadAsync();
        third.IsError.Should().BeTrue();
        third.Text.Should().Be("ERR max number of commands per second exceeded");

        // The connection stays open; tokens refill at 1 per second.
        await Wait.UntilAsync(
            async () => (await connection.CommandAsync("PING")).AsString() == "PONG",
            "the bucket refills");
    }
}
