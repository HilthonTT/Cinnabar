using Cinnabar.Client;
using Cinnabar.Protocol;
using Cinnabar.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Cinnabar.IntegrationTests;

/// <summary>WATCH-based optimistic transactions and EXEC isolation across real connections.</summary>
public sealed class WatchTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    private Keyspace Keyspace => _server.Server.Services.GetRequiredService<Keyspace>();

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task ChangeFromAnotherConnection_AbortsExec()
    {
        await using RawConnection a = await RawConnection.ConnectAsync(_server.Port);
        await using RawConnection b = await RawConnection.ConnectAsync(_server.Port);
        await a.CommandAsync("SET", "k", "1");

        (await a.CommandAsync("WATCH", "k")).AsString().Should().Be("OK");
        await a.CommandAsync("MULTI");
        (await a.CommandAsync("SET", "k", "from-a")).AsString().Should().Be("QUEUED");
        await b.CommandAsync("SET", "k", "from-b");

        (await a.CommandAsync("EXEC")).Type.Should().Be(RespType.NullArray);
        (await Client.GetAsync("k")).Should().Be("from-b");
    }

    [Fact]
    public async Task OptimisticTransaction_SucceedsWhenUncontended()
    {
        await Client.SetAsync("balance", "100");

        IReadOnlyList<RespValue>? result = await Client.ExecuteTransactionAsync(["balance"], async read =>
        {
            long balance = long.Parse((await read(["GET", "balance"])).AsString()!);
            return [["SET", "balance", (balance - 30).ToString()]];
        });

        result.Should().ContainSingle().Which.AsString().Should().Be("OK");
        (await Client.GetAsync("balance")).Should().Be("70");
    }

    [Fact]
    public async Task OptimisticTransaction_ReturnsNullWhenAWatchedKeyChanges()
    {
        await Client.SetAsync("balance", "100");
        await using RedisClient other = _server.CreateClient();

        IReadOnlyList<RespValue>? result = await Client.ExecuteTransactionAsync(["balance"], async read =>
        {
            long balance = long.Parse((await read(["GET", "balance"])).AsString()!);
            await other.SetAsync("balance", "0"); // someone else spends it meanwhile
            return [["SET", "balance", (balance - 30).ToString()]];
        });

        result.Should().BeNull();
        (await Client.GetAsync("balance")).Should().Be("0");
    }

    [Fact]
    public async Task ConcurrentReadModifyWrite_WithRetries_LosesNoUpdates()
    {
        // A non-atomic "GET then SET" increment; WATCH makes it safe under contention.
        await Client.SetAsync("counter", "0");
        const int Workers = 8;
        const int IncrementsEach = 25;

        await Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
        {
            await using RedisClient client = _server.CreateClient();
            for (int i = 0; i < IncrementsEach; i++)
            {
                IReadOnlyList<RespValue>? result;
                do
                {
                    result = await client.ExecuteTransactionAsync(["counter"], async read =>
                    {
                        long value = long.Parse((await read(["GET", "counter"])).AsString()!);
                        return [["SET", "counter", (value + 1).ToString()]];
                    });
                }
                while (result is null);
            }
        }))).WithTimeout(TimeSpan.FromSeconds(30));

        (await Client.GetAsync("counter")).Should().Be((Workers * IncrementsEach).ToString());
    }

    [Fact]
    public async Task ExecIsIsolated_OtherClientsNeverSeeHalfATransaction()
    {
        // One client moves a token between two keys inside MULTI/EXEC; another keeps reading
        // both. With isolation, exactly one key holds the token at every read.
        await Client.MSetAsync([new("left", "1"), new("right", "0")]);
        using var stop = new CancellationTokenSource();

        Task writer = Task.Run(async () =>
        {
            await using RedisClient client = _server.CreateClient();
            bool toRight = true;
            while (!stop.IsCancellationRequested)
            {
                await client.ExecuteTransactionAsync(
                [
                    ["SET", toRight ? "left" : "right", "0"],
                    ["SET", toRight ? "right" : "left", "1"],
                ]);
                toRight = !toRight;
            }
        });

        await using RawConnection raw = await RawConnection.ConnectAsync(_server.Port);
        for (int i = 0; i < 300; i++)
        {
            // Two GETs in a transaction of our own: without isolation they could run between
            // the writer's two SETs and see "00" or "11".
            await raw.SendAsync("MULTI");
            await raw.SendAsync("GET", "left");
            await raw.SendAsync("GET", "right");
            await raw.SendAsync("EXEC");
            await raw.ReadAsync();
            await raw.ReadAsync();
            await raw.ReadAsync();
            IReadOnlyList<RespValue> values = (await raw.ReadAsync()).AsArray();

            (values[0].AsString() + values[1].AsString()).Should().BeOneOf("10", "01");
        }

        stop.Cancel();
        await writer.WithTimeout();
    }

    [Fact]
    public async Task Disconnecting_ReleasesWatches()
    {
        await using (RawConnection connection = await RawConnection.ConnectAsync(_server.Port))
        {
            await connection.CommandAsync("WATCH", "a", "b");
            Keyspace.WatchedKeyCount.Should().Be(2);
        }

        await Wait.UntilAsync(() => Keyspace.WatchedKeyCount == 0, "the server releases a closed connection's watches");
    }

    [Fact]
    public async Task BlockedClient_DoesNotStallTransactions()
    {
        await using RawConnection blocked = await RawConnection.ConnectAsync(_server.Port);
        await blocked.SendAsync("BLPOP", "jobs", "0");
        await _server.UntilWatchedKeysAsync(1);

        IReadOnlyList<RespValue> results = await Client.ExecuteTransactionAsync([["RPUSH", "jobs", "j1"]]).WithTimeout();

        results.Single().AsLong().Should().Be(1);
        (await blocked.ReadAsync()).AsStringList().Should().Equal("jobs", "j1");
    }
}
