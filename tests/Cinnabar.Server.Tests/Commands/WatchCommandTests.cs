using Cinnabar.Server.Commands;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

/// <summary>WATCH/UNWATCH and the isolation of EXEC.</summary>
public sealed class WatchCommandTests : DispatcherTestBase
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private async Task<string> RunOn(ClientSession session, params string[] parts) =>
        (await H.Dispatch(session, parts)).ToString();

    [Fact]
    public async Task Exec_runs_when_watched_keys_are_untouched()
    {
        await Run("SET", "k", "1");
        (await Run("WATCH", "k")).Should().Be("+OK\r\n");
        await Run("MULTI");
        await Run("INCR", "k");

        (await Run("EXEC")).Should().Be("*1\r\n:2\r\n");
    }

    [Fact]
    public async Task Exec_aborts_when_another_client_changes_a_watched_key()
    {
        ClientSession other = H.NewSession();
        await Run("SET", "k", "1");
        await Run("WATCH", "k");
        await Run("MULTI");
        await Run("SET", "k", "mine");

        await RunOn(other, "SET", "k", "theirs");

        (await Run("EXEC")).Should().Be("*-1\r\n");
        (await Run("GET", "k")).Should().Be(Bulk("theirs"));
        H.Session.InTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task Own_change_before_multi_also_aborts()
    {
        await Run("WATCH", "k");
        await Run("SET", "k", "1");
        await Run("MULTI");
        await Run("SET", "k", "2");

        (await Run("EXEC")).Should().Be("*-1\r\n");
        (await Run("GET", "k")).Should().Be(Bulk("1"));
    }

    [Fact]
    public async Task Changes_to_unwatched_keys_do_not_abort()
    {
        await Run("WATCH", "k");
        await RunOn(H.NewSession(), "SET", "other", "x");
        await Run("MULTI");
        await Run("SET", "k", "v");

        (await Run("EXEC")).Should().Be("*1\r\n+OK\r\n");
    }

    [Fact]
    public async Task Expiry_of_a_watched_key_aborts()
    {
        await Run("SET", "k", "v", "PX", "100");
        await Run("WATCH", "k");
        H.Time.Advance(TimeSpan.FromSeconds(1));
        await Run("MULTI");
        await Run("SET", "k", "new");

        (await Run("EXEC")).Should().Be("*-1\r\n");
    }

    [Fact]
    public async Task Flushall_aborts_watchers()
    {
        await Run("SET", "k", "v");
        await Run("WATCH", "k");
        await RunOn(H.NewSession(), "FLUSHALL");
        await Run("MULTI");
        await Run("SET", "k", "new");

        (await Run("EXEC")).Should().Be("*-1\r\n");
    }

    [Fact]
    public async Task Exec_ends_the_watch_either_way()
    {
        await Run("WATCH", "k");
        await Run("MULTI");
        (await Run("EXEC")).Should().Be("*0\r\n");

        await RunOn(H.NewSession(), "SET", "k", "changed");
        await Run("MULTI");
        await Run("GET", "k");

        (await Run("EXEC")).Should().Be("*1\r\n" + Bulk("changed"));
        H.Keyspace.WatchedKeyCount.Should().Be(0);
    }

    [Fact]
    public async Task Unwatch_and_discard_forget_watches()
    {
        await Run("WATCH", "a", "b");
        (await Run("UNWATCH")).Should().Be("+OK\r\n");
        H.Session.WatchedKeys.Should().BeEmpty();

        await Run("WATCH", "a");
        await Run("MULTI");
        (await Run("DISCARD")).Should().Be("+OK\r\n");

        H.Session.WatchedKeys.Should().BeEmpty();
        H.Keyspace.WatchedKeyCount.Should().Be(0);
    }

    [Fact]
    public async Task Execabort_also_forgets_watches()
    {
        await Run("WATCH", "k");
        await Run("MULTI");
        await Run("NOSUCHCOMMAND");

        (await Run("EXEC")).Should().StartWith("-EXECABORT");
        H.Keyspace.WatchedKeyCount.Should().Be(0);
    }

    [Fact]
    public async Task Watch_inside_multi_is_rejected()
    {
        await Run("MULTI");

        (await Run("WATCH", "k")).Should().Be("-ERR WATCH inside MULTI is not allowed\r\n");
        H.Session.InTransaction.Should().BeTrue();
    }

    [Fact]
    public async Task Watch_needs_a_key()
    {
        (await Run("WATCH")).Should().Be("-ERR wrong number of arguments for 'watch' command\r\n");
    }

    [Fact]
    public async Task Exec_waits_for_commands_already_running()
    {
        var gate = H.Get<TransactionGate>();
        SharedExclusiveGate.Releaser inFlight = await gate.EnterSharedAsync();
        await Run("MULTI");
        await Run("SET", "k", "v");

        Task<Reply> exec = H.Dispatch("EXEC");
        await Task.Delay(100);
        exec.IsCompleted.Should().BeFalse("a command from another client is still running");

        inFlight.Dispose();
        (await exec.WaitAsync(WaitLimit)).ToString().Should().Be("*1\r\n+OK\r\n");
    }

    [Fact]
    public async Task Commands_wait_while_a_transaction_runs()
    {
        var gate = H.Get<TransactionGate>();
        SharedExclusiveGate.Releaser transaction = await gate.EnterExclusiveAsync();

        Task<Reply> get = H.Dispatch(H.NewSession(), "GET", "k");
        await Task.Delay(100);
        get.IsCompleted.Should().BeFalse("EXEC holds the gate exclusively");

        transaction.Dispose();
        (await get.WaitAsync(WaitLimit)).ToString().Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Psync_does_not_need_the_gate()
    {
        // A full sync can take a while; it must not make EXEC (and everything behind it) wait.
        var gate = H.Get<TransactionGate>();
        using (await gate.EnterExclusiveAsync())
        {
            Reply reply = await H.Dispatch(H.NewSession(), "PSYNC", "?", "-1").WaitAsync(WaitLimit);

            reply.IsEmpty.Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_client_blocked_in_blpop_does_not_stall_exec()
    {
        Task<Reply> blocked = H.Dispatch(H.NewSession(), "BLPOP", "queue", "0");
        await Task.Delay(100);
        await Run("MULTI");
        await Run("RPUSH", "queue", "job");

        (await H.Dispatch("EXEC").WaitAsync(WaitLimit)).ToString().Should().Be("*1\r\n:1\r\n");
        (await blocked.WaitAsync(WaitLimit)).ToString().Should().Be(BulkArray("queue", "job"));
    }
}
