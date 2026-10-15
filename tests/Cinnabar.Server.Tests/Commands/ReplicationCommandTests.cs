using Cinnabar.Server.Commands;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class ReplicationCommandTests : DispatcherTestBase
{
    [Theory]
    [InlineData("listening-port", "6380")]
    [InlineData("capa", "psync2")]
    public async Task Replconf_options_reply_ok(string option, string value)
    {
        (await Run("REPLCONF", option, value)).Should().Be("+OK\r\n");
    }

    [Fact]
    public async Task Replconf_ack_sends_nothing()
    {
        Reply reply = await H.Dispatch("REPLCONF", "ACK", "100");

        reply.IsEmpty.Should().BeTrue();
        reply.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Replconf_ack_with_bad_offset_is_an_error()
    {
        (await Run("REPLCONF", "ACK", "abc")).Should().Be("-ERR value is not an integer or out of range\r\n");
    }

    [Fact]
    public async Task Wait_without_replicas_returns_zero_immediately()
    {
        await Run("SET", "k", "v");

        Task<string> wait = Run("WAIT", "1", "0");

        (await wait.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Wait_with_negative_count_is_an_error()
    {
        (await Run("WAIT", "-1", "0")).Should().Be("-ERR value is out of range, must be positive\r\n");
    }

    [Fact]
    public async Task Wait_with_negative_timeout_is_an_error()
    {
        (await Run("WAIT", "1", "-5")).Should().Be("-ERR timeout is negative\r\n");
    }

    [Fact]
    public async Task Wait_counts_replicas_that_acknowledged_the_write()
    {
        ClientSession replica = H.NewSession();
        await H.Master.StartFullSyncAsync(replica, CancellationToken.None);
        await Run("SET", "k", "v");

        await H.Dispatch(replica, "REPLCONF", "ACK", H.Session.LastWriteOffset.ToString());

        (await Run("WAIT", "1", "1000")).Should().Be(":1\r\n");
    }

    [Fact]
    public async Task Wait_times_out_with_count_of_acknowledged_replicas()
    {
        ClientSession replica = H.NewSession();
        await H.Master.StartFullSyncAsync(replica, CancellationToken.None);
        await Run("SET", "k", "v");

        (await Run("WAIT", "1", "50")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Wait_inside_exec_does_not_block()
    {
        ClientSession replica = H.NewSession();
        await H.Master.StartFullSyncAsync(replica, CancellationToken.None);
        await Run("MULTI");
        await Run("SET", "k", "v");
        await Run("WAIT", "1", "0");

        (await Run("EXEC").WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("*2\r\n+OK\r\n:0\r\n");
    }
}
