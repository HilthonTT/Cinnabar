using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class StreamGroupCommandTests : DispatcherTestBase
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private async Task Seed(params string[] ids)
    {
        foreach (string id in ids)
        {
            await Run("XADD", "s", id, "f", id);
        }
    }

    private static string Entry(string id) => $"*2\r\n{Bulk(id)}{BulkArray("f", id)}";

    private static string StreamReply(string key, params string[] ids) =>
        $"*1\r\n*2\r\n{Bulk(key)}*{ids.Length}\r\n" + string.Concat(ids.Select(Entry));

    [Fact]
    public async Task Xgroup_create_and_busygroup()
    {
        await Seed("1-1");

        (await Run("XGROUP", "CREATE", "s", "g", "$")).Should().Be("+OK\r\n");
        (await Run("XGROUP", "CREATE", "s", "g", "0")).Should().Be("-BUSYGROUP Consumer Group name already exists\r\n");
        (await Run("XGROUP", "CREATE", "missing", "g", "0")).Should().StartWith("-ERR The XGROUP subcommand requires the key to exist");
        (await Run("XGROUP", "CREATE", "new", "g", "$", "MKSTREAM")).Should().Be("+OK\r\n");
        (await Run("TYPE", "new")).Should().Be("+stream\r\n");
    }

    [Fact]
    public async Task Xgroup_create_dollar_replicates_the_resolved_id()
    {
        await Seed("5-1");

        Reply reply = await H.Dispatch("XGROUP", "CREATE", "s", "g", "$");

        reply.PropagateAs!.ToString().Should().Be("XGROUP CREATE s g 5-1");
    }

    [Theory]
    [InlineData("-ERR unknown subcommand 'BOGUS'. Try XGROUP HELP.\r\n", "BOGUS")]
    [InlineData("-ERR wrong number of arguments for 'xgroup|create' command\r\n", "CREATE", "s", "g")]
    [InlineData("-ERR wrong number of arguments for 'xgroup|destroy' command\r\n", "DESTROY", "s")]
    [InlineData("-ERR Invalid stream ID specified as stream command argument\r\n", "CREATE", "s", "g", "nope")]
    public async Task Xgroup_validates_arguments(string expected, params string[] args)
    {
        await Seed("1-1");

        (await Run(["XGROUP", .. args])).Should().Be(expected);
    }

    [Fact]
    public async Task Xreadgroup_delivers_new_entries_and_tracks_them()
    {
        await Seed("1-1", "2-1");
        await Run("XGROUP", "CREATE", "s", "g", "0");

        (await Run("XREADGROUP", "GROUP", "g", "alice", "COUNT", "1", "STREAMS", "s", ">")).Should().Be(StreamReply("s", "1-1"));
        (await Run("XREADGROUP", "GROUP", "g", "bob", "STREAMS", "s", ">")).Should().Be(StreamReply("s", "2-1"));
        (await Run("XREADGROUP", "GROUP", "g", "bob", "STREAMS", "s", ">")).Should().Be("*-1\r\n");

        (await Run("XPENDING", "s", "g")).Should().Be(
            "*4\r\n:2\r\n" + Bulk("1-1") + Bulk("2-1") + "*2\r\n" + BulkArray("alice", "1") + BulkArray("bob", "1"));
    }

    [Fact]
    public async Task Xreadgroup_history_returns_pending_entries_with_empty_streams_included()
    {
        await Seed("1-1");
        await Run("XGROUP", "CREATE", "s", "g", "0");
        await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", ">");

        (await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", "0")).Should().Be(StreamReply("s", "1-1"));
        (await Run("XREADGROUP", "GROUP", "g", "bob", "STREAMS", "s", "0")).Should().Be(StreamReply("s"));
    }

    [Fact]
    public async Task Xack_and_xpending_extended()
    {
        await Seed("1-1", "2-1");
        await Run("XGROUP", "CREATE", "s", "g", "0");
        await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", ">");
        H.Time.Advance(TimeSpan.FromSeconds(3));

        (await Run("XACK", "s", "g", "1-1", "9-9")).Should().Be(":1\r\n");
        (await Run("XPENDING", "s", "g", "-", "+", "10")).Should().Be(
            "*1\r\n*4\r\n" + Bulk("2-1") + Bulk("alice") + ":3000\r\n:1\r\n");
        (await Run("XPENDING", "s", "g", "IDLE", "5000", "-", "+", "10")).Should().Be("*0\r\n");
        (await Run("XPENDING", "s", "g", "-", "+", "10", "bob")).Should().Be("*0\r\n");
    }

    [Fact]
    public async Task Xpending_summary_of_an_empty_group()
    {
        await Seed("1-1");
        await Run("XGROUP", "CREATE", "s", "g", "$");

        (await Run("XPENDING", "s", "g")).Should().Be("*4\r\n:0\r\n$-1\r\n$-1\r\n*-1\r\n");
    }

    [Fact]
    public async Task Missing_group_is_nogroup()
    {
        await Seed("1-1");

        (await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", ">"))
            .Should().Be("-NOGROUP No such key 's' or consumer group 'g' in XREADGROUP with GROUP option\r\n");
        (await Run("XPENDING", "s", "g")).Should().Be("-NOGROUP No such key 's' or consumer group 'g'\r\n");
        (await Run("XACK", "s", "g", "1-1")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Xclaim_takes_over_idle_entries()
    {
        await Seed("1-1", "2-1");
        await Run("XGROUP", "CREATE", "s", "g", "0");
        await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", ">");
        H.Time.Advance(TimeSpan.FromSeconds(10));

        (await Run("XCLAIM", "s", "g", "bob", "5000", "1-1")).Should().Be("*1\r\n" + Entry("1-1"));
        (await Run("XCLAIM", "s", "g", "carol", "5000", "1-1", "JUSTID")).Should().Be("*0\r\n");
        (await Run("XCLAIM", "s", "g", "carol", "0", "2-1", "JUSTID")).Should().Be(BulkArray("2-1"));
        (await Run("XCLAIM", "s", "g", "bob", "0", "1-1", "BOGUS")).Should().Be("-ERR Unrecognized XCLAIM option 'BOGUS'\r\n");
    }

    [Fact]
    public async Task Xclaim_replicates_as_a_deterministic_xclaim()
    {
        await Seed("1-1");
        await Run("XGROUP", "CREATE", "s", "g", "0");
        await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", ">");
        H.Time.Advance(TimeSpan.FromSeconds(10));

        Reply reply = await H.Dispatch("XCLAIM", "s", "g", "bob", "5000", "1-1");

        reply.PropagateAs!.ToString().Should().Be($"XCLAIM s g bob 0 1-1 TIME {H.NowMs} FORCE LASTID 1-1");
    }

    [Fact]
    public async Task Failed_xclaim_is_not_replicated()
    {
        await Seed("1-1");
        await Run("XGROUP", "CREATE", "s", "g", "0");
        await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", ">");
        long before = H.Master.Offset;

        await Run("XCLAIM", "s", "g", "bob", "60000", "1-1");

        H.Master.Offset.Should().Be(before);
    }

    [Fact]
    public async Task Xreadgroup_replicates_deliveries_as_xclaim()
    {
        await Seed("1-1", "2-1");
        await Run("XGROUP", "CREATE", "s", "g", "0");
        long before = H.Master.Offset;

        await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", ">");

        var expected = new Command("XCLAIM", "s", "g", "alice", "0", "1-1", "2-1", "TIME", H.NowMs.ToString(), "RETRYCOUNT", "1", "FORCE", "LASTID", "2-1");
        H.Master.Offset.Should().Be(before + expected.Encode().Length);
    }

    [Fact]
    public async Task Xautoclaim_returns_cursor_entries_and_deleted_ids()
    {
        await Seed("1-1", "2-1", "3-1");
        await Run("XGROUP", "CREATE", "s", "g", "0");
        await Run("XREADGROUP", "GROUP", "g", "alice", "STREAMS", "s", ">");
        H.Time.Advance(TimeSpan.FromSeconds(10));

        (await Run("XAUTOCLAIM", "s", "g", "bob", "1000", "0", "COUNT", "2")).Should().Be(
            "*3\r\n" + Bulk("3-1") + "*2\r\n" + Entry("1-1") + Entry("2-1") + "*0\r\n");
        (await Run("XAUTOCLAIM", "s", "g", "bob", "1000", "3-1", "JUSTID")).Should().Be(
            "*3\r\n" + Bulk("0-0") + BulkArray("3-1") + "*0\r\n");
        (await Run("XAUTOCLAIM", "s", "g", "bob", "1000", "0", "COUNT", "0")).Should().Be("-ERR COUNT must be > 0\r\n");
    }

    [Fact]
    public async Task Blocking_xreadgroup_wakes_on_xadd()
    {
        await Run("XGROUP", "CREATE", "s", "g", "$", "MKSTREAM");

        Task<Reply> blocked = H.Dispatch(H.NewSession(), "XREADGROUP", "GROUP", "g", "alice", "BLOCK", "0", "STREAMS", "s", ">");
        await Task.Delay(50);
        blocked.IsCompleted.Should().BeFalse();

        await Run("XADD", "s", "7-1", "f", "7-1");

        (await blocked.WaitAsync(WaitLimit)).ToString().Should().Be(StreamReply("s", "7-1"));
    }

    [Fact]
    public async Task Blocking_xreadgroup_times_out_with_nil()
    {
        await Run("XGROUP", "CREATE", "s", "g", "$", "MKSTREAM");

        (await Run("XREADGROUP", "GROUP", "g", "alice", "BLOCK", "50", "STREAMS", "s", ">")).Should().Be("*-1\r\n");
    }

    [Fact]
    public async Task Xreadgroup_inside_exec_does_not_block()
    {
        await Run("XGROUP", "CREATE", "s", "g", "$", "MKSTREAM");
        await Run("MULTI");
        await Run("XREADGROUP", "GROUP", "g", "alice", "BLOCK", "0", "STREAMS", "s", ">");

        (await H.Dispatch("EXEC").WaitAsync(WaitLimit)).ToString().Should().Be("*1\r\n*-1\r\n");
    }

    [Fact]
    public async Task Xreadgroup_is_a_write_and_fails_on_a_replica()
    {
        await using var replica = new DispatcherHarness(s =>
        {
            s.Replication.MasterHost = "127.0.0.1";
            s.Replication.MasterPort = 1;
        });

        (await replica.Run("XREADGROUP", "GROUP", "g", "c", "STREAMS", "s", ">")).Should().StartWith("-READONLY");
    }
}
