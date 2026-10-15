using Cinnabar.Client;

namespace Cinnabar.IntegrationTests;

public sealed class AuthTests : IAsyncLifetime
{
    private const string Password = "secret";

    private TestServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await TestServer.StartAsync(s => s.Security.RequirePass = Password, Password);

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task ClientWithoutPassword_GetsNoAuth()
    {
        RedisClient client = _server.CreateClient();

        Func<Task> act = () => client.GetAsync("k");

        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("NOAUTH");
    }

    [Fact]
    public async Task ClientWithPassword_Works()
    {
        await _server.Client.SetAsync("k", "v");
        (await _server.Client.GetAsync("k")).Should().Be("v");
    }

    [Fact]
    public async Task WrongPassword_ThrowsWrongPassOnFirstCommand()
    {
        RedisClient client = _server.CreateClient(o => o.Password = "not-the-password");

        Func<Task> act = () => client.PingAsync();

        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("WRONGPASS");
    }

    [Fact]
    public async Task WrongUsername_ThrowsWrongPass()
    {
        RedisClient client = _server.CreateClient(o =>
        {
            o.Username = "mallory";
            o.Password = Password;
        });

        Func<Task> act = () => client.PingAsync();

        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("WRONGPASS");
    }

    [Fact]
    public async Task ConnectionString_WithUserAndPassword_Works()
    {
        RedisClient client = _server.CreateClient(o => o.ConnectionString = $"default:{Password}@127.0.0.1:{_server.Port}");

        await client.SetAsync("cs", "ok");
        (await client.GetAsync("cs")).Should().Be("ok");
    }

    [Fact]
    public async Task RawConnection_AuthCommandUnlocksSession()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        (await connection.CommandAsync("GET", "k")).Text.Should().StartWith("NOAUTH");
        (await connection.CommandAsync("AUTH", "wrong")).Text.Should().StartWith("WRONGPASS");
        (await connection.CommandAsync("AUTH", Password)).AsString().Should().Be("OK");
        (await connection.CommandAsync("PING")).AsString().Should().Be("PONG");
    }

    [Fact]
    public async Task Quit_IsAllowedBeforeAuth()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        (await connection.CommandAsync("QUIT")).AsString().Should().Be("OK");
        (await connection.IsClosedByServerAsync()).Should().BeTrue();
    }
}

public sealed class AuthWithoutPasswordTests : IAsyncLifetime
{
    private TestServer _server = null!;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Auth_WhenNoPasswordConfigured_IsAnError()
    {
        Func<Task> act = () => _server.Client.ExecuteAsync("AUTH", "anything");

        (await act.Should().ThrowAsync<RedisException>()).Which.Message.Should().Contain("without any password configured");
    }
}
