using Cinnabar.Client;

namespace Cinnabar.IntegrationTests;

/// <summary>Streams and their consumer groups survive a restart and reach replicas on full sync.</summary>
public sealed class StreamDurabilityTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "cinnabar-tests", "streams-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }

    private Task<TestServer> StartPersistentAsync() => TestServer.StartAsync(s =>
    {
        s.Persistence.Directory = _directory;
        s.Persistence.LoadOnStartup = true;
        s.Persistence.SaveOnShutdown = true;
    });

    [Fact]
    public async Task Restart_RestoresStreamsAndConsumerGroups()
    {
        string first;
        string second;
        await using (TestServer server = await StartPersistentAsync())
        {
            RedisClient client = server.Client;
            first = await client.XAddAsync("events", [new("n", "1")]);
            second = await client.XAddAsync("events", [new("n", "2")]);
            await client.XGroupCreateAsync("events", "workers", "0");
            await client.XReadGroupAsync("events", "workers", "alice", count: 1);
            await client.XGroupCreateAsync("empty", "g", "$", makeStream: true);
        }

        await using TestServer restarted = await StartPersistentAsync();
        RedisClient after = restarted.Client;

        (await after.XRangeAsync("events")).Select(e => e.Id).Should().Equal(first, second);
        RedisPendingSummary pending = await after.XPendingAsync("events", "workers");
        pending.Count.Should().Be(1);
        pending.SmallestId.Should().Be(first);
        pending.Consumers.Should().Equal(new[] { new KeyValuePair<string, long>("alice", 1) });

        // The group carries on from where it was: only the second entry is new.
        (await after.XReadGroupAsync("events", "workers", "bob")).Select(e => e.Id).Should().Equal(second);
        (await after.XLenAsync("empty")).Should().Be(0);
        (await after.TypeAsync("empty")).Should().Be("stream");
    }

    [Fact]
    public async Task FullSync_CopiesStreamsAndGroups()
    {
        await using TestServer master = await TestServer.StartAsync();
        string id = await master.Client.XAddAsync("s", [new("f", "v")]);
        await master.Client.XGroupCreateAsync("s", "g", "0");
        await master.Client.XReadGroupAsync("s", "g", "c");

        await using TestServer replica = await Replication.StartReplicaAsync(master);

        (await replica.Client.XRangeAsync("s")).Single().Id.Should().Be(id);
        (await replica.Client.XPendingAsync("s", "g")).Count.Should().Be(1);

        // Later group activity replicates on top of the synced state.
        await master.Client.XAckAsync("s", "g", id);
        await Replication.UntilAsync(
            async () => (await replica.Client.XPendingAsync("s", "g")).Count == 0,
            "the XACK reaches the replica");
    }
}
