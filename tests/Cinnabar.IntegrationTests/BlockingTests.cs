using Cinnabar.Client;
using Cinnabar.Protocol;

namespace Cinnabar.IntegrationTests;

/// <summary>BLPOP and XREAD BLOCK across several client connections.</summary>
public sealed class BlockingTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task BLPop_WakesWhenAnotherClientPushes()
    {
        RedisClient waiterClient = _server.CreateClient();
        Task<(string Key, string Value)?> waiter = waiterClient.BLPopAsync("jobs", 10);

        await _server.UntilWatchedKeysAsync(1);
        waiter.IsCompleted.Should().BeFalse();

        await Client.RPushAsync("jobs", "job-1");

        (await waiter.WithTimeout()).Should().Be(("jobs", "job-1"));
        (await Client.LLenAsync("jobs")).Should().Be(0);
    }

    [Fact]
    public async Task BLPop_ReturnsNullAfterTimeout()
    {
        var started = DateTime.UtcNow;
        (await Client.BLPopAsync("empty", 0.3)).Should().BeNull();
        (DateTime.UtcNow - started).Should().BeGreaterThan(TimeSpan.FromMilliseconds(250));
        _server.WatchedKeyCount().Should().Be(0, "a timed-out waiter unregisters");
    }

    [Fact]
    public async Task BLPop_TwoWaitersTwoPushes_EachGetsOneElement()
    {
        RedisClient waiters = _server.CreateClient();

        // Each waiter also watches its own dummy key so we can tell when both are blocked.
        Task<(string Key, string Value)?> first = waiters.BLPopAsync(["shared", "dummy-1"], TimeSpan.FromSeconds(10));
        Task<(string Key, string Value)?> second = waiters.BLPopAsync(["shared", "dummy-2"], TimeSpan.FromSeconds(10));
        await _server.UntilWatchedKeysAsync(3);

        await Client.RPushAsync("shared", "a");
        await Client.RPushAsync("shared", "b");

        (string Key, string Value)?[] results = await Task.WhenAll(first, second).WithTimeout();
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
        results.Select(r => r!.Value.Value).Should().BeEquivalentTo(["a", "b"]);
        (await Client.LLenAsync("shared")).Should().Be(0);
    }

    [Fact]
    public async Task BLPop_OnePushOfTwoElements_WakesBothWaiters()
    {
        RedisClient waiters = _server.CreateClient();
        Task<(string Key, string Value)?> first = waiters.BLPopAsync(["shared", "dummy-1"], TimeSpan.FromSeconds(10));
        Task<(string Key, string Value)?> second = waiters.BLPopAsync(["shared", "dummy-2"], TimeSpan.FromSeconds(10));
        await _server.UntilWatchedKeysAsync(3);

        await Client.RPushAsync("shared", "a", "b");

        (string Key, string Value)?[] results = await Task.WhenAll(first, second).WithTimeout();
        results.Select(r => r!.Value.Value).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task XReadBlock_WithDollar_WakesOnXAddFromAnotherClient()
    {
        await Client.XAddAsync("events", "1-0", [new("old", "entry")]);

        RedisClient readerClient = _server.CreateClient();
        Task<RespValue> reader = readerClient.ExecuteAsync("XREAD", "BLOCK", "10000", "STREAMS", "events", "$");
        await _server.UntilWatchedKeysAsync(1);

        string id = await Client.XAddAsync("events", [new("new", "entry")]);

        RespValue reply = await reader.WithTimeout();
        IReadOnlyList<RespValue> streams = reply.AsArray();
        streams.Should().ContainSingle();
        streams[0].AsArray()[0].AsString().Should().Be("events");
        IReadOnlyList<RespValue> entries = streams[0].AsArray()[1].AsArray();
        entries.Should().ContainSingle("only entries newer than '$' are returned");
        entries[0].AsArray()[0].AsString().Should().Be(id);
        entries[0].AsArray()[1].AsStringList().Should().Equal("new", "entry");
    }

    [Fact]
    public async Task XReadBlock_OnMissingStream_WakesOnFirstXAdd()
    {
        RedisClient readerClient = _server.CreateClient();
        Task<RespValue> reader = readerClient.ExecuteAsync("XREAD", "BLOCK", "10000", "STREAMS", "fresh", "$");
        await _server.UntilWatchedKeysAsync(1);

        string id = await Client.XAddAsync("fresh", [new("k", "v")]);

        RespValue reply = await reader.WithTimeout();
        reply.AsArray()[0].AsArray()[1].AsArray()[0].AsArray()[0].AsString().Should().Be(id);
    }

    [Fact]
    public async Task XReadBlock_TimesOutWithNull()
    {
        RespValue reply = await Client.ExecuteAsync("XREAD", "BLOCK", "200", "STREAMS", "quiet", "$");
        reply.IsNull.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectedBLPopWaiter_DoesNotStealLaterElement()
    {
        RawConnection blocked = await RawConnection.ConnectAsync(_server.Port);
        await blocked.SendAsync("BLPOP", "orphan", "0");
        await _server.UntilWatchedKeysAsync(1);

        blocked.Close();
        await blocked.DisposeAsync();

        // The server polls for disconnects every 100 ms.
        await _server.UntilWatchedKeysAsync(0);

        await Client.RPushAsync("orphan", "precious");
        (await Client.LPopAsync("orphan")).Should().Be("precious");
    }
}
