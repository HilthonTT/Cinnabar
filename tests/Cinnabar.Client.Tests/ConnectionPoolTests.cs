using System.Net;
using System.Net.Sockets;
using Cinnabar.Client.Internal;
using Cinnabar.Client.Tests.Infrastructure;

namespace Cinnabar.Client.Tests;

public class ConnectionPoolTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Healthy_connection_is_reused_for_sequential_commands()
    {
        await using var server = new FakeRedisServer();
        await using var client = new RedisClient(server.Options());

        (await client.PingAsync()).Should().BeTrue();
        (await client.EchoAsync("hi")).Should().Be("hi");

        server.AcceptedConnections.Should().Be(1);
        server.Received.Should().OnlyContain(c => c.Connection == 0);
    }

    [Fact]
    public async Task Returned_connection_is_kept_idle()
    {
        await using var server = new FakeRedisServer();
        await using var pool = new ConnectionPool(server.Options());

        await using (ConnectionPool.Lease lease = await pool.RentAsync(CancellationToken.None))
        {
            (await lease.Connection.ExecuteAsync(["PING"], CancellationToken.None)).AsString().Should().Be("PONG");
        }

        pool.IdleCount.Should().Be(1);
    }

    [Fact]
    public async Task Disposing_a_lease_twice_returns_it_once()
    {
        await using var server = new FakeRedisServer();
        await using var pool = new ConnectionPool(server.Options(o => o.PoolSize = 1));

        ConnectionPool.Lease lease = await pool.RentAsync(CancellationToken.None);
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        pool.IdleCount.Should().Be(1);
        await using ConnectionPool.Lease again = await pool.RentAsync(CancellationToken.None);
        again.Connection.Should().BeSameAs(lease.Connection);
    }

    [Fact]
    public async Task Cancelled_command_discards_connection_so_stale_reply_is_never_read()
    {
        var commandReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStaleReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleReplySent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = new FakeRedisServer(async (command, ct) =>
        {
            if (command.Connection == 0 && command.Parts[0] == "GET")
            {
                if (command.Parts[1] == "slow")
                {
                    // Hold the reply until the client has given up, then send it late.
                    commandReceived.TrySetResult();
                    await releaseStaleReply.Task.WaitAsync(ct);
                    staleReplySent.TrySetResult();
                    return FakeRedisServer.Reply("$5\r\nstale\r\n");
                }

                return FakeRedisServer.Reply("$5\r\nWRONG\r\n");
            }

            return FakeRedisServer.Reply(command.Parts[0] == "GET" ? "$5\r\nfresh\r\n" : "+OK\r\n");
        });
        await using var client = new RedisClient(server.Options(o => o.PoolSize = 1));

        using var cts = new CancellationTokenSource();
        Task<string?> slow = client.GetAsync("slow", cts.Token);
        await commandReceived.Task.WaitAsync(Timeout);
        cts.Cancel();

        Func<Task> awaitSlow = () => slow.WaitAsync(Timeout);
        await awaitSlow.Should().ThrowAsync<OperationCanceledException>();

        releaseStaleReply.SetResult();
        await staleReplySent.Task.WaitAsync(Timeout);
        await Task.Delay(100); // let the stale bytes reach the client socket

        string? value = await client.GetAsync("next").WaitAsync(Timeout);

        value.Should().Be("fresh");
        server.AcceptedConnections.Should().Be(2);
    }

    [Fact]
    public async Task Connection_is_reused_after_completed_command_but_not_after_cancelled_one()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeRedisServer(async (command, ct) =>
        {
            if (command.Parts[0] == "BLOCK")
            {
                received.TrySetResult();
                await hold.Task.WaitAsync(ct);
            }

            return FakeRedisServer.Reply("+PONG\r\n");
        });
        await using var pool = new ConnectionPool(server.Options(o => o.PoolSize = 1));

        await using (ConnectionPool.Lease lease = await pool.RentAsync(CancellationToken.None))
        {
            await lease.Connection.ExecuteAsync(["PING"], CancellationToken.None);
            lease.Connection.IsHealthy.Should().BeTrue();
        }

        pool.IdleCount.Should().Be(1);

        using var cts = new CancellationTokenSource();
        ConnectionPool.Lease blocked = await pool.RentAsync(CancellationToken.None);
        Task execute = blocked.Connection.ExecuteAsync(["BLOCK"], cts.Token);
        await received.Task.WaitAsync(Timeout);
        cts.Cancel();
        Func<Task> act = () => execute.WaitAsync(Timeout);
        await act.Should().ThrowAsync<OperationCanceledException>();

        blocked.Connection.IsHealthy.Should().BeFalse();
        await blocked.DisposeAsync();
        pool.IdleCount.Should().Be(0);
        hold.SetResult();
    }

    [Fact]
    public async Task Auth_is_sent_first_on_each_new_connection()
    {
        await using var server = new FakeRedisServer();
        await using var client = new RedisClient(server.Options(o =>
        {
            o.Username = "alice";
            o.Password = "s3cret";
        }));

        await client.PingAsync();
        await client.PingAsync();

        server.Received.Select(c => string.Join(' ', c.Parts)).Should().Equal("AUTH alice s3cret", "PING", "PING");
    }

    [Fact]
    public async Task Auth_uses_default_username()
    {
        await using var server = new FakeRedisServer();
        await using var client = new RedisClient(server.Options(o => o.Password = "pw"));

        await client.PingAsync();

        server.Received.First().Parts.Should().Equal("AUTH", "default", "pw");
    }

    [Fact]
    public async Task No_auth_without_password()
    {
        await using var server = new FakeRedisServer();
        await using var client = new RedisClient(server.Options());

        await client.PingAsync();

        server.Received.Select(c => c.Parts[0]).Should().Equal("PING");
    }

    [Fact]
    public async Task Auth_error_surfaces_as_RedisException()
    {
        await using var server = new FakeRedisServer((command, _) => Task.FromResult<byte[]?>(command.Parts[0] == "AUTH"
            ? FakeRedisServer.Reply("-WRONGPASS invalid username-password pair or user is disabled.\r\n")
            : FakeRedisServer.Reply("+PONG\r\n")));
        await using var client = new RedisClient(server.Options(o => o.Password = "bad"));

        Func<Task> act = () => client.PingAsync().WaitAsync(Timeout);

        RedisException exception = (await act.Should().ThrowAsync<RedisException>()).Which;
        exception.ErrorCode.Should().Be("WRONGPASS");
        server.Received.Select(c => c.Parts[0]).Should().Equal("AUTH");
    }

    [Fact]
    public async Task Failed_auth_releases_the_pool_slot()
    {
        await using var server = new FakeRedisServer((command, _) => Task.FromResult<byte[]?>(
            FakeRedisServer.Reply(command.Parts[0] == "AUTH" ? "-WRONGPASS nope\r\n" : "+PONG\r\n")));
        await using var client = new RedisClient(server.Options(o =>
        {
            o.Password = "bad";
            o.PoolSize = 1;
            o.PoolTimeout = TimeSpan.FromMilliseconds(500);
        }));

        for (int i = 0; i < 3; i++)
        {
            Func<Task> act = () => client.PingAsync().WaitAsync(Timeout);
            await act.Should().ThrowAsync<RedisException>();
        }
    }

    [Fact]
    public async Task Server_error_reply_throws_RedisException_and_keeps_connection()
    {
        await using var server = new FakeRedisServer((command, _) => Task.FromResult<byte[]?>(command.Parts[0] == "INCR"
            ? FakeRedisServer.Reply("-ERR value is not an integer or out of range\r\n")
            : FakeRedisServer.Reply("+PONG\r\n")));
        await using var client = new RedisClient(server.Options());

        Func<Task> act = () => client.IncrementAsync("k");
        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("ERR");

        (await client.PingAsync()).Should().BeTrue();
        server.AcceptedConnections.Should().Be(1);
    }

    [Fact]
    public async Task Connection_refused_throws_RedisConnectionException()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await using var client = new RedisClient(new RedisClientOptions { Host = "127.0.0.1", Port = port });

        Func<Task> act = () => client.PingAsync().WaitAsync(Timeout);

        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    [Fact]
    public async Task Server_closing_connection_throws_RedisConnectionException_and_next_command_reconnects()
    {
        int calls = 0;
        await using var server = new FakeRedisServer((command, _) =>
        {
            // First command: reply with garbage that is not valid RESP.
            return Task.FromResult<byte[]?>(Interlocked.Increment(ref calls) == 1
                ? FakeRedisServer.Reply("!garbage\r\n")
                : FakeRedisServer.Reply("+PONG\r\n"));
        });
        await using var client = new RedisClient(server.Options());

        Func<Task> act = () => client.PingAsync().WaitAsync(Timeout);
        await act.Should().ThrowAsync<RedisConnectionException>();

        (await client.PingAsync().WaitAsync(Timeout)).Should().BeTrue();
        server.AcceptedConnections.Should().Be(2);
    }

    [Fact]
    public async Task Pool_exhaustion_throws_RedisConnectionException()
    {
        await using var server = new FakeRedisServer();
        await using var pool = new ConnectionPool(server.Options(o =>
        {
            o.PoolSize = 1;
            o.PoolTimeout = TimeSpan.FromMilliseconds(200);
        }));

        await using ConnectionPool.Lease held = await pool.RentAsync(CancellationToken.None);

        Func<Task> act = () => pool.RentAsync(CancellationToken.None).WaitAsync(Timeout);

        await act.Should().ThrowAsync<RedisConnectionException>().WithMessage("*PoolSize = 1*");
    }

    [Fact]
    public async Task Pool_slot_freed_by_returning_lease_can_be_rented_again()
    {
        await using var server = new FakeRedisServer();
        await using var pool = new ConnectionPool(server.Options(o =>
        {
            o.PoolSize = 1;
            o.PoolTimeout = TimeSpan.FromSeconds(5);
        }));

        ConnectionPool.Lease first = await pool.RentAsync(CancellationToken.None);
        Task<ConnectionPool.Lease> waiting = pool.RentAsync(CancellationToken.None);
        await Task.Delay(50);
        waiting.IsCompleted.Should().BeFalse();

        await first.DisposeAsync();

        await using ConnectionPool.Lease second = await waiting.WaitAsync(Timeout);
        second.Connection.Should().BeSameAs(first.Connection);
    }

    [Fact]
    public async Task Concurrent_commands_never_exceed_pool_size()
    {
        int active = 0;
        int maxActive = 0;
        await using var server = new FakeRedisServer(async (command, ct) =>
        {
            int now = Interlocked.Increment(ref active);
            InterlockedMax(ref maxActive, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref active);
            return RespEncoderReply(command.Parts[1]);
        });
        await using var client = new RedisClient(server.Options(o => o.PoolSize = 3));

        string?[] results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => client.EchoAsync($"m{i}"))).WaitAsync(Timeout);

        results.Should().Equal(Enumerable.Range(0, 12).Select(i => $"m{i}"));
        maxActive.Should().BeLessThanOrEqualTo(3);
        server.AcceptedConnections.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task Disposed_pool_rejects_rent()
    {
        await using var server = new FakeRedisServer();
        var pool = new ConnectionPool(server.Options());
        await pool.DisposeAsync();

        Func<Task> act = () => pool.RentAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static byte[] RespEncoderReply(string text) => Cinnabar.Protocol.RespEncoder.BulkString(text);

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
