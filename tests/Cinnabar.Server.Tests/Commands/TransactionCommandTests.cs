using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class TransactionCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Commands_inside_multi_are_queued()
    {
        (await Run("MULTI")).Should().Be("+OK\r\n");
        (await Run("SET", "k", "v")).Should().Be("+QUEUED\r\n");

        (await Run("EXISTS", "k")).Should().Be("+QUEUED\r\n");
        H.Keyspace.Exists("k").Should().BeFalse();
    }

    [Fact]
    public async Task Exec_returns_array_of_replies()
    {
        await Run("MULTI");
        await Run("SET", "k", "v");
        await Run("INCR", "n");
        await Run("GET", "k");

        (await Run("EXEC")).Should().Be("*3\r\n+OK\r\n:1\r\n$1\r\nv\r\n");
        H.Session.InTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task Exec_with_no_commands_returns_empty_array()
    {
        await Run("MULTI");

        (await Run("EXEC")).Should().Be("*0\r\n");
    }

    [Fact]
    public async Task Runtime_error_inside_exec_does_not_stop_other_commands()
    {
        await Run("SET", "s", "text");
        await Run("MULTI");
        await Run("INCR", "s");
        await Run("SET", "k", "v");

        (await Run("EXEC")).Should().Be("*2\r\n-ERR value is not an integer or out of range\r\n+OK\r\n");
        (await Run("GET", "k")).Should().Be("$1\r\nv\r\n");
    }

    [Fact]
    public async Task Nested_multi_is_an_error_but_keeps_transaction()
    {
        await Run("MULTI");

        (await Run("MULTI")).Should().Be("-ERR MULTI calls can not be nested\r\n");
        await Run("SET", "k", "v");
        (await Run("EXEC")).Should().Be("*1\r\n+OK\r\n");
    }

    [Fact]
    public async Task Exec_without_multi_is_an_error()
    {
        (await Run("EXEC")).Should().Be("-ERR EXEC without MULTI\r\n");
    }

    [Fact]
    public async Task Discard_without_multi_is_an_error()
    {
        (await Run("DISCARD")).Should().Be("-ERR DISCARD without MULTI\r\n");
    }

    [Fact]
    public async Task Discard_drops_queued_commands()
    {
        await Run("MULTI");
        await Run("SET", "k", "v");

        (await Run("DISCARD")).Should().Be("+OK\r\n");

        H.Session.InTransaction.Should().BeFalse();
        (await Run("EXISTS", "k")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Unknown_command_while_queueing_aborts_exec()
    {
        await Run("MULTI");
        await Run("SET", "k", "v");
        (await Run("NOPE")).Should().StartWith("-ERR unknown command");

        (await Run("EXEC")).Should().Be("-EXECABORT Transaction discarded because of previous errors.\r\n");
        (await Run("EXISTS", "k")).Should().Be(":0\r\n");
        H.Session.InTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task Arity_error_while_queueing_aborts_exec()
    {
        await Run("MULTI");
        await Run("SET", "k", "v");
        (await Run("GET")).Should().StartWith("-ERR wrong number of arguments");

        (await Run("EXEC")).Should().StartWith("-EXECABORT");
        (await Run("EXISTS", "k")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task New_transaction_after_execabort_starts_clean()
    {
        await Run("MULTI");
        await Run("NOPE");
        await Run("EXEC");

        await Run("MULTI");
        await Run("SET", "k", "v");
        (await Run("EXEC")).Should().Be("*1\r\n+OK\r\n");
    }

    [Fact]
    public async Task Writes_inside_exec_are_propagated()
    {
        await Run("MULTI");
        await Run("SET", "k", "v");
        long beforeExec = H.Master.Offset;

        await Run("EXEC");

        H.Master.Offset.Should().BeGreaterThan(beforeExec);
        beforeExec.Should().Be(0, "queueing does not propagate");
    }

    [Fact]
    public async Task Quit_inside_multi_runs_immediately()
    {
        await Run("MULTI");

        (await H.Dispatch("QUIT")).CloseConnection.Should().BeTrue();
    }
}
