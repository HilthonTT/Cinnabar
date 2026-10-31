using Cinnabar.Client;

namespace Cinnabar.IntegrationTests;

/// <summary>The client's typed stream consumer-group API against a real server.</summary>
public sealed class ClientStreamGroupTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private Task<string> Add(string key, string value) => Client.XAddAsync(key, [new("v", value)]);

    [Fact]
    public async Task ReadGroup_Ack_And_Pending()
    {
        await Client.XGroupCreateAsync("jobs", "workers", "$", makeStream: true);
        string first = await Add("jobs", "a");
        string second = await Add("jobs", "b");

        List<RedisStreamEntry> delivered = await Client.XReadGroupAsync("jobs", "workers", "alice", count: 10);
        delivered.Select(e => e.Id).Should().Equal(first, second);
        delivered[0].Fields.Should().Equal(new[] { new KeyValuePair<string, string>("v", "a") });

        RedisPendingSummary summary = await Client.XPendingAsync("jobs", "workers");
        summary.Count.Should().Be(2);
        summary.SmallestId.Should().Be(first);
        summary.LargestId.Should().Be(second);
        summary.Consumers.Should().Equal(new[] { new KeyValuePair<string, long>("alice", 2) });

        (await Client.XAckAsync("jobs", "workers", first)).Should().Be(1);

        List<RedisPendingEntry> pending = await Client.XPendingAsync("jobs", "workers", "-", "+", 10);
        pending.Should().ContainSingle().Which.Should().Match<RedisPendingEntry>(p =>
            p.Id == second && p.Consumer == "alice" && p.DeliveryCount == 1);
    }

    [Fact]
    public async Task ReadGroup_WithNothingNew_ReturnsEmpty()
    {
        await Client.XGroupCreateAsync("s", "g", "$", makeStream: true);

        (await Client.XReadGroupAsync("s", "g", "c")).Should().BeEmpty();
        (await Client.XReadGroupAsync("s", "g", "c", block: TimeSpan.FromMilliseconds(50))).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadGroup_Blocking_WakesOnXAdd()
    {
        await Client.XGroupCreateAsync("s", "g", "$", makeStream: true);
        await using RedisClient other = _server.CreateClient();

        Task<List<RedisStreamEntry>> read = other.XReadGroupAsync("s", "g", "c", block: TimeSpan.Zero);
        await _server.UntilWatchedKeysAsync(1);
        string id = await Add("s", "x");

        (await read.WithTimeout()).Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public async Task ReadGroup_SeveralStreams_ReportsEachKey()
    {
        await Client.XGroupCreateAsync("a", "g", "0", makeStream: true);
        await Client.XGroupCreateAsync("b", "g", "0", makeStream: true);
        await Add("a", "1");
        await Add("b", "2");

        List<RedisStreamRead> reads = await Client.XReadGroupAsync("g", "c", [new("a", ">"), new("b", ">")]);

        reads.Select(r => r.Key).Should().Equal("a", "b");
        reads.Should().OnlyContain(r => r.Entries.Count == 1);
    }

    [Fact]
    public async Task Claim_And_AutoClaim_TransferOwnership()
    {
        await Client.XGroupCreateAsync("s", "g", "0", makeStream: true);
        string first = await Add("s", "1");
        string second = await Add("s", "2");
        await Client.XReadGroupAsync("s", "g", "alice");

        List<RedisStreamEntry> claimed = await Client.XClaimAsync("s", "g", "bob", TimeSpan.Zero, first);
        claimed.Should().ContainSingle().Which.Id.Should().Be(first);

        RedisAutoClaimResult auto = await Client.XAutoClaimAsync("s", "g", "carol", TimeSpan.Zero);
        auto.NextStartId.Should().Be("0-0");
        auto.Entries.Select(e => e.Id).Should().Equal(first, second);
        auto.DeletedIds.Should().BeEmpty();

        (await Client.XPendingAsync("s", "g")).Consumers.Should().Equal(new[] { new KeyValuePair<string, long>("carol", 2) });
    }

    [Fact]
    public async Task GroupManagement()
    {
        await Add("s", "1");
        await Client.XGroupCreateAsync("s", "g", "0");

        Func<Task> duplicate = () => Client.XGroupCreateAsync("s", "g");
        await duplicate.Should().ThrowAsync<RedisException>().WithMessage("BUSYGROUP*");

        (await Client.XGroupCreateConsumerAsync("s", "g", "c")).Should().BeTrue();
        (await Client.XGroupCreateConsumerAsync("s", "g", "c")).Should().BeFalse();
        await Client.XReadGroupAsync("s", "g", "c");
        (await Client.XGroupDeleteConsumerAsync("s", "g", "c")).Should().Be(1);

        await Client.XGroupSetIdAsync("s", "g", "0");
        (await Client.XReadGroupAsync("s", "g", "d")).Should().HaveCount(1);

        (await Client.XGroupDestroyAsync("s", "g")).Should().BeTrue();
        (await Client.XGroupDestroyAsync("s", "g")).Should().BeFalse();
    }

    [Fact]
    public async Task XRead_ReadsAfterId_AndTimesOutEmpty()
    {
        string first = await Add("s", "1");
        string second = await Add("s", "2");

        List<RedisStreamRead> reads = await Client.XReadAsync([new("s", first)]);
        reads.Single().Entries.Single().Id.Should().Be(second);

        (await Client.XReadAsync([new("s", "$")], block: TimeSpan.FromMilliseconds(50))).Should().BeEmpty();
    }
}
