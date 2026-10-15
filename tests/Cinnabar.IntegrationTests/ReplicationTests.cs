using Cinnabar.Client;
using Cinnabar.Protocol;

namespace Cinnabar.IntegrationTests;

/// <summary>Helpers for master/replica pairs in one process.</summary>
internal static class Replication
{
    /// <summary>Starts a replica of <paramref name="master"/> and waits until its link is up.</summary>
    public static async Task<TestServer> StartReplicaAsync(TestServer master, string? masterAuth = null)
    {
        TestServer replica = await TestServer.StartAsync(s =>
        {
            s.Replication.MasterHost = "127.0.0.1";
            s.Replication.MasterPort = master.Port;
            s.Replication.MasterAuth = masterAuth;
        });

        try
        {
            await Wait.UntilAsync(
                async () => (await replica.Client.InfoAsync("replication"))?.Contains("master_link_status:up") == true,
                "the replica's link to the master is up",
                TimeSpan.FromSeconds(10));
        }
        catch
        {
            await replica.DisposeAsync();
            throw;
        }

        return replica;
    }

    public static Task UntilAsync(Func<Task<bool>> condition, string because) =>
        Wait.UntilAsync(condition, because, TimeSpan.FromSeconds(5));
}

/// <summary>Master/replica pair: full sync, command streaming, read-only replica, WAIT.</summary>
public sealed class ReplicationTests : IAsyncLifetime
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
    public async Task FullSync_CopiesDataWrittenBeforeReplicaConnected()
    {
        await Master.SetAsync("before:string", "value");
        await Master.SetAsync("before:ttl", "value", TimeSpan.FromMinutes(10));
        await Master.RPushAsync("before:list", "a", "b", "c");
        await Master.IncrementByAsync("before:counter", 41);

        await ConnectReplicaAsync();

        (await Replica.GetAsync("before:string")).Should().Be("value");
        (await Replica.LRangeAsync("before:list")).Should().Equal("a", "b", "c");
        (await Replica.GetAsync("before:counter")).Should().Be("41");
        (await Replica.TimeToLiveAsync("before:ttl")).Should().BeGreaterThan(TimeSpan.FromMinutes(9));
        (await Replica.DbSizeAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Replica_ReportsRoleAndLink()
    {
        await ConnectReplicaAsync();

        string? info = await Replica.InfoAsync("replication");
        info.Should().Contain("role:slave").And.Contain($"master_port:{_master.Port}").And.Contain("master_link_status:up");
    }

    [Fact]
    public async Task Master_ReportsConnectedReplica()
    {
        await ConnectReplicaAsync();

        await Replication.UntilAsync(
            async () => (await Master.InfoAsync("replication"))?.Contains("connected_slaves:1") == true,
            "the master lists one replica");
        (await Master.InfoAsync("replication")).Should().Contain("role:master").And.Contain("slave0:ip=127.0.0.1");
    }

    [Fact]
    public async Task Set_IsStreamed()
    {
        await ConnectReplicaAsync();

        await Master.SetAsync("k", "v");

        await Replication.UntilAsync(async () => await Replica.GetAsync("k") == "v", "SET reaches the replica");
    }

    [Fact]
    public async Task Incr_IsStreamed()
    {
        await ConnectReplicaAsync();

        await Master.IncrementAsync("n");
        await Master.IncrementByAsync("n", 9);
        await Master.DecrementAsync("n");

        await Replication.UntilAsync(async () => await Replica.GetAsync("n") == "9", "INCR/INCRBY/DECR reach the replica");
    }

    [Fact]
    public async Task ListPushAndPop_AreStreamed()
    {
        await ConnectReplicaAsync();

        await Master.RPushAsync("l", "a", "b", "c", "d");
        await Master.LPushAsync("l", "z");
        await Master.LPopAsync("l");
        await Master.RPopAsync("l");
        await Master.LPopAsync("l", 1);

        await Replication.UntilAsync(
            async () => (await Replica.LRangeAsync("l")).SequenceEqual(["b", "c"]),
            "list writes reach the replica");
    }

    [Fact]
    public async Task Del_IsStreamed()
    {
        await Master.SetAsync("doomed", "x");
        await Master.SetAsync("kept", "y");
        await ConnectReplicaAsync();

        (await Master.DeleteAsync("doomed")).Should().Be(1);

        await Replication.UntilAsync(async () => await Replica.ExistsAsync("doomed") == 0, "DEL reaches the replica");
        (await Replica.GetAsync("kept")).Should().Be("y");
    }

    [Fact]
    public async Task Expire_IsStreamedWithTtl()
    {
        await ConnectReplicaAsync();
        await Master.SetAsync("session", "data");

        await Master.ExecuteAsync("EXPIRE", "session", "300");

        await Replication.UntilAsync(async () => await Replica.TimeToLiveAsync("session") is not null, "the TTL reaches the replica");
        (await Replica.TimeToLiveAsync("session")).Should().BeGreaterThan(TimeSpan.FromSeconds(290));
        (await Replica.GetAsync("session")).Should().Be("data");
    }

    [Fact]
    public async Task SetWithExpiry_IsStreamedWithTtl()
    {
        await ConnectReplicaAsync();

        await Master.SetAsync("temp", "v", TimeSpan.FromMinutes(5));

        await Replication.UntilAsync(async () => await Replica.GetAsync("temp") == "v", "SET PX reaches the replica");
        (await Replica.TimeToLiveAsync("temp")).Should().BeGreaterThan(TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task Persist_IsStreamed()
    {
        await ConnectReplicaAsync();
        await Master.SetAsync("p", "v", TimeSpan.FromMinutes(5));
        await Replication.UntilAsync(async () => await Replica.TimeToLiveAsync("p") is not null, "the TTL reaches the replica");

        await Master.PersistAsync("p");

        await Replication.UntilAsync(async () => await Replica.TimeToLiveAsync("p") is null, "PERSIST reaches the replica");
    }

    [Fact]
    public async Task XAddWithAutoId_ProducesSameIdOnReplica()
    {
        await ConnectReplicaAsync();

        string first = await Master.XAddAsync("stream", [new("n", "1")]);
        string second = await Master.XAddAsync("stream", [new("n", "2")]);

        await Replication.UntilAsync(async () => await Replica.XLenAsync("stream") == 2, "XADD reaches the replica");
        List<RedisStreamEntry> entries = await Replica.XRangeAsync("stream");
        entries.Select(e => e.Id).Should().Equal(first, second);
        entries[1].Fields.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("n", "2"));
    }

    [Fact]
    public async Task BLPopOnMaster_RemovesElementOnReplica()
    {
        await ConnectReplicaAsync();
        await Master.RPushAsync("q", "first", "second");
        await Replication.UntilAsync(async () => await Replica.LLenAsync("q") == 2, "RPUSH reaches the replica");

        (await Master.BLPopAsync("q", 1)).Should().Be(("q", "first"));

        await Replication.UntilAsync(
            async () => (await Replica.LRangeAsync("q")).SequenceEqual(["second"]),
            "the BLPOP is replicated as a pop");
    }

    [Fact]
    public async Task BlockedBLPopOnMaster_WokenByPush_RemovesElementOnReplica()
    {
        await ConnectReplicaAsync();
        RedisClient waiterClient = _master.CreateClient();
        Task<(string Key, string Value)?> waiter = waiterClient.BLPopAsync("q", 10);
        await _master.UntilWatchedKeysAsync(1);

        await Master.RPushAsync("q", "job");

        (await waiter.WithTimeout()).Should().Be(("q", "job"));
        await Master.SetAsync("marker", "1");
        await Replication.UntilAsync(async () => await Replica.GetAsync("marker") == "1", "the marker reaches the replica");
        (await Replica.LLenAsync("q")).Should().Be(0, "the replica must apply the RPUSH and the pop");
    }

    [Fact]
    public async Task FlushDb_IsStreamed()
    {
        await Master.SetAsync("a", "1");
        await ConnectReplicaAsync();

        await Master.FlushDbAsync();

        await Replication.UntilAsync(async () => await Replica.DbSizeAsync() == 0, "FLUSHDB reaches the replica");
    }

    [Fact]
    public async Task TransactionWrites_AreStreamed()
    {
        await ConnectReplicaAsync();

        await Master.ExecuteTransactionAsync([["SET", "tx", "1"], ["INCR", "tx"]]);

        await Replication.UntilAsync(async () => await Replica.GetAsync("tx") == "2", "transaction writes reach the replica");
    }

    [Fact]
    public async Task ManyWrites_ArriveInOrder()
    {
        await ConnectReplicaAsync();

        for (int i = 0; i < 200; i++)
        {
            await Master.RPushAsync("ordered", i.ToString());
        }

        await Replication.UntilAsync(async () => await Replica.LLenAsync("ordered") == 200, "every push reaches the replica");
        (await Replica.LRangeAsync("ordered")).Should().Equal(Enumerable.Range(0, 200).Select(i => i.ToString()));
    }

    [Fact]
    public async Task ReadOnlyCommandsAndFailedWrites_AreNotStreamed()
    {
        await ConnectReplicaAsync();
        await Master.SetAsync("s", "v");
        await FluentActions.Awaiting(() => Master.RPushAsync("s", "x")).Should().ThrowAsync<RedisException>();
        (await Master.ExpireAsync("missing", TimeSpan.FromSeconds(5))).Should().BeFalse();
        await Master.SetAsync("marker", "1");

        await Replication.UntilAsync(async () => await Replica.GetAsync("marker") == "1", "the marker reaches the replica");
        (await Replica.GetAsync("s")).Should().Be("v");
        (await Replica.DbSizeAsync()).Should().Be(2);
    }

    [Fact]
    public async Task WritesToReplica_FailWithReadOnly()
    {
        await ConnectReplicaAsync();

        Func<Task> act = () => Replica.SetAsync("k", "v");

        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("READONLY");
        (await Replica.GetAsync("k")).Should().BeNull();
    }

    [Theory]
    [InlineData("RPUSH", "l", "x")]
    [InlineData("DEL", "k")]
    [InlineData("INCR", "n")]
    [InlineData("XADD", "s", "*", "f", "v")]
    [InlineData("BLPOP", "l", "1")]
    [InlineData("FLUSHDB")]
    public async Task EveryWriteCommandOnReplica_FailsWithReadOnly(params string[] command)
    {
        await ConnectReplicaAsync();

        Func<Task> act = () => Replica.ExecuteAsync(command);

        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("READONLY");
    }

    [Fact]
    public async Task ReadsOnReplica_Work()
    {
        await Master.SetAsync("k", "v");
        await ConnectReplicaAsync();

        (await Replica.GetAsync("k")).Should().Be("v");
        (await Replica.ExistsAsync("k")).Should().Be(1);
        (await Replica.PingAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Wait_AfterWrite_ReturnsOneReplica()
    {
        await ConnectReplicaAsync();
        await using RawConnection connection = await RawConnection.ConnectAsync(_master.Port);

        (await connection.CommandAsync("SET", "durable", "yes")).AsString().Should().Be("OK");
        RespValue acknowledged = await connection.CommandAsync("WAIT", "1", "2000");

        acknowledged.AsLong().Should().Be(1);
        (await Replica.GetAsync("durable")).Should().Be("yes", "WAIT returned only after the replica applied the write");
    }

    [Fact]
    public async Task Wait_RepeatedAfterSeveralWrites_ReturnsOneReplica()
    {
        await ConnectReplicaAsync();
        await using RawConnection connection = await RawConnection.ConnectAsync(_master.Port);

        for (int round = 0; round < 3; round++)
        {
            await connection.CommandAsync("RPUSH", "l", round.ToString());
            await connection.CommandAsync("XADD", "s", "*", "round", round.ToString());
            (await connection.CommandAsync("WAIT", "1", "2000")).AsLong().Should().Be(1, $"round {round}");
        }

        (await Replica.LLenAsync("l")).Should().Be(3);
        (await Replica.XLenAsync("s")).Should().Be(3);
    }

    [Fact]
    public async Task Wait_WithoutReplicas_ReturnsZeroImmediately()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_master.Port);
        await connection.CommandAsync("SET", "k", "v");

        (await connection.CommandAsync("WAIT", "1", "100")).AsLong().Should().Be(0);
    }

    [Fact]
    public async Task Wait_OnReplica_IsAnError()
    {
        await ConnectReplicaAsync();

        Func<Task> act = () => Replica.ExecuteAsync("WAIT", "1", "100");

        await act.Should().ThrowAsync<RedisException>();
    }

    [Fact]
    public async Task ReplicaDisposed_MasterForgetsIt()
    {
        await ConnectReplicaAsync();
        await Replication.UntilAsync(
            async () => (await Master.InfoAsync("replication"))?.Contains("connected_slaves:1") == true,
            "the master lists the replica");

        await _replica!.DisposeAsync();
        _replica = null;

        await Replication.UntilAsync(
            async () => (await Master.InfoAsync("replication"))?.Contains("connected_slaves:0") == true,
            "the master drops the replica");
        await Master.SetAsync("still", "works");
    }

    [Fact]
    public async Task TwoReplicas_BothReceiveWrites()
    {
        await ConnectReplicaAsync();
        await using TestServer second = await Replication.StartReplicaAsync(_master);

        await Master.SetAsync("fan", "out");

        await Replication.UntilAsync(async () => await Replica.GetAsync("fan") == "out", "the first replica gets the write");
        await Replication.UntilAsync(async () => await second.Client.GetAsync("fan") == "out", "the second replica gets the write");
    }
}

/// <summary>Replication to a master that requires a password.</summary>
public sealed class AuthenticatedReplicationTests : IAsyncLifetime
{
    private const string Password = "master-secret";

    private TestServer _master = null!;

    public async Task InitializeAsync() =>
        _master = await TestServer.StartAsync(s => s.Security.RequirePass = Password, Password);

    public async Task DisposeAsync() => await _master.DisposeAsync();

    [Fact]
    public async Task ReplicaWithMasterAuth_SyncsAndStreams()
    {
        await _master.Client.SetAsync("before", "1");

        await using TestServer replica = await Replication.StartReplicaAsync(_master, masterAuth: Password);

        (await replica.Client.GetAsync("before")).Should().Be("1");
        await _master.Client.SetAsync("after", "2");
        await Replication.UntilAsync(async () => await replica.Client.GetAsync("after") == "2", "streamed writes reach the replica");
    }

    [Fact]
    public async Task ReplicaWithMasterAuth_WaitReturnsOne()
    {
        await using TestServer replica = await Replication.StartReplicaAsync(_master, masterAuth: Password);
        await using RawConnection connection = await RawConnection.ConnectAsync(_master.Port);
        (await connection.CommandAsync("AUTH", Password)).AsString().Should().Be("OK");

        await connection.CommandAsync("SET", "k", "v");

        (await connection.CommandAsync("WAIT", "1", "2000")).AsLong().Should().Be(1);
    }

    [Fact]
    public async Task ReplicaWithoutMasterAuth_NeverComesUp()
    {
        await using TestServer replica = await TestServer.StartAsync(s =>
        {
            s.Replication.MasterHost = "127.0.0.1";
            s.Replication.MasterPort = _master.Port;
        });

        // Give it a few handshake attempts (back-off starts at 250 ms).
        DateTime until = DateTime.UtcNow.AddSeconds(1.5);
        while (DateTime.UtcNow < until)
        {
            (await replica.Client.InfoAsync("replication")).Should().Contain("master_link_status:down");
            await Task.Delay(100);
        }
    }
}
