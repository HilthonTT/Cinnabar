using System.Text;
using Cinnabar.Client;
using Cinnabar.Protocol;

namespace Cinnabar.IntegrationTests;

/// <summary>Hashes, sets, sorted sets and binary values across full sync and the command stream.</summary>
public sealed class CollectionReplicationTests : IAsyncLifetime
{
    private TestServer _master = null!;
    private TestServer? _replica;

    private RedisClient Master => _master.Client;

    private RedisClient Replica => _replica!.Client;

    public async Task InitializeAsync() => _master = await TestServer.StartAsync();

    public async Task DisposeAsync()
    {
        if (_replica is not null)
        {
            await _replica.DisposeAsync();
        }

        await _master.DisposeAsync();
    }

    private async Task ConnectReplicaAsync() => _replica = await Replication.StartReplicaAsync(_master);

    [Fact]
    public async Task FullSync_CopiesCollections()
    {
        await Master.HSetAsync("h", [new("a", "1"), new("b", "2")]);
        await Master.SAddAsync("s", "x", "y");
        await Master.ZAddAsync("z", [new("low", -1.5), new("high", double.PositiveInfinity)]);
        await Master.ExecuteAsync("EXPIRE", "h", "600");

        await ConnectReplicaAsync();

        (await Replica.HGetAllAsync("h")).Should().BeEquivalentTo(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        (await Replica.SMembersAsync("s")).Should().BeEquivalentTo(["x", "y"]);
        (await Replica.ZRangeAsync("z")).Should().Equal(
            new RedisSortedSetEntry("low", -1.5),
            new RedisSortedSetEntry("high", double.PositiveInfinity));
        (await Replica.TimeToLiveAsync("h")).Should().BeGreaterThan(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public async Task CollectionWrites_AreStreamed()
    {
        await ConnectReplicaAsync();

        await Master.HSetAsync("h", "f", "v");
        await Master.SAddAsync("s", "a", "b");
        await Master.ZAddAsync("z", "m", 2);
        await Master.ZIncrByAsync("z", "m", 0.5);
        await Master.SMoveAsync("s", "s2", "a");
        await Master.ExecuteAsync("SUNIONSTORE", "u", "s", "s2");

        await Replication.UntilAsync(async () => await Replica.ExistsAsync("u") == 1, "the writes reach the replica");
        (await Replica.HGetAsync("h", "f")).Should().Be("v");
        (await Replica.ZScoreAsync("z", "m")).Should().Be(2.5);
        (await Replica.SMembersAsync("u")).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task SPop_RemovesTheSameMembersOnTheReplica()
    {
        await Master.SAddAsync("s", [.. Enumerable.Range(0, 20).Select(i => $"m{i}")]);
        await ConnectReplicaAsync();

        RespValue popped = await Master.ExecuteAsync("SPOP", "s", "5");
        await Master.SetAsync("marker", "done");

        await Replication.UntilAsync(async () => await Replica.GetAsync("marker") == "done", "the writes reach the replica");
        (await Replica.SMembersAsync("s")).Should().BeEquivalentTo(await Master.SMembersAsync("s"));
        (await Replica.SMembersAsync("s")).Should().NotIntersectWith(popped.AsStringList());
    }

    [Fact]
    public async Task HIncrByFloat_ReplicatesTheExactValue()
    {
        await ConnectReplicaAsync();

        await Master.HIncrByFloatAsync("h", "f", 0.1);
        double result = await Master.HIncrByFloatAsync("h", "f", 0.2);

        await Replication.UntilAsync(
            async () => await Replica.HGetAsync("h", "f") == await Master.HGetAsync("h", "f"),
            "the replica stores the master's value");
        result.Should().BeApproximately(0.3, 1e-12);
    }


    [Fact]
    public async Task BinaryValues_ReplicateByteForByte()
    {
        await ConnectReplicaAsync();
        byte[] value = [0x00, 0xFF, 0xC3, 0x28, (byte)'\r', (byte)'\n', 0x80];

        await using (RawConnection connection = await RawConnection.ConnectAsync(_master.Port))
        {
            await connection.WriteAsync([.. "*3\r\n$3\r\nSET\r\n$3\r\nbin\r\n"u8.ToArray(), .. RespEncoder.BulkString(value)]);
            (await connection.ReadAsync()).AsString().Should().Be("OK");
        }

        await Replication.UntilAsync(async () => await Replica.ExistsAsync("bin") == 1, "SET reaches the replica");

        await using RawConnection replica = await RawConnection.ConnectAsync(_replica!.Port);
        await replica.SendAsync("GET", "bin");
        byte[] expected = RespEncoder.BulkString(value);
        (await replica.ReadBytesAsync(expected.Length)).Should().Equal(expected);
        Encoding.ASCII.GetString(expected, 0, 3).Should().Be("$7\r");
    }
}
