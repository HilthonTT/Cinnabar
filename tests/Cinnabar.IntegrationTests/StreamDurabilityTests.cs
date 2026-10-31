using Cinnabar.Client;
using Cinnabar.Protocol;

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

    /// <summary>XPENDING key group: the pending count.</summary>
    private static async Task<long> PendingCountAsync(RedisClient client, string key, string group) =>
        (await client.ExecuteAsync("XPENDING", key, group)).AsArray()[0].AsLong();

    /// <summary>XREADGROUP for new entries on one stream: the delivered IDs.</summary>
    private static async Task<List<string>> ReadGroupIdsAsync(RedisClient client, string key, string group, string consumer, int count = 10)
    {
        RespValue reply = await client.ExecuteAsync("XREADGROUP", "GROUP", group, consumer, "COUNT", count.ToString(), "STREAMS", key, ">");
        return reply.IsNull
            ? []
            : [.. reply.AsArray()[0].AsArray()[1].AsArray().Select(entry => entry.AsArray()[0].AsString()!)];
    }

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
            await client.ExecuteAsync("XGROUP", "CREATE", "events", "workers", "0");
            (await ReadGroupIdsAsync(client, "events", "workers", "alice", count: 1)).Should().Equal(first);
            await client.ExecuteAsync("XGROUP", "CREATE", "empty", "g", "$", "MKSTREAM");
        }

        await using TestServer restarted = await StartPersistentAsync();
        RedisClient after = restarted.Client;

        (await after.XRangeAsync("events")).Select(e => e.Id).Should().Equal(first, second);
        IReadOnlyList<RespValue> pending = (await after.ExecuteAsync("XPENDING", "events", "workers")).AsArray();
        pending[0].AsLong().Should().Be(1);
        pending[1].AsString().Should().Be(first);
        pending[3].AsArray().Single().AsStringList().Should().Equal("alice", "1");

        // The group carries on from where it was: only the second entry is new.
        (await ReadGroupIdsAsync(after, "events", "workers", "bob")).Should().Equal(second);
        (await after.XLenAsync("empty")).Should().Be(0);
        (await after.TypeAsync("empty")).Should().Be("stream");
    }

    [Fact]
    public async Task FullSync_CopiesStreamsAndGroups()
    {
        await using TestServer master = await TestServer.StartAsync();
        string id = await master.Client.XAddAsync("s", [new("f", "v")]);
        await master.Client.ExecuteAsync("XGROUP", "CREATE", "s", "g", "0");
        await ReadGroupIdsAsync(master.Client, "s", "g", "c");

        await using TestServer replica = await Replication.StartReplicaAsync(master);

        (await replica.Client.XRangeAsync("s")).Single().Id.Should().Be(id);
        (await PendingCountAsync(replica.Client, "s", "g")).Should().Be(1);

        // Later group activity replicates on top of the synced state.
        await master.Client.ExecuteAsync("XACK", "s", "g", id);
        await Replication.UntilAsync(
            async () => await PendingCountAsync(replica.Client, "s", "g") == 0,
            "the XACK reaches the replica");
    }
}
