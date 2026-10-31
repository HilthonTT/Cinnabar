using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class ListEditCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Lpushx_and_rpushx_need_an_existing_list()
    {
        (await Run("LPUSHX", "l", "a")).Should().Be(":0\r\n");
        (await Run("EXISTS", "l")).Should().Be(":0\r\n");

        await Run("RPUSH", "l", "b");
        (await Run("LPUSHX", "l", "a")).Should().Be(":2\r\n");
        (await Run("RPUSHX", "l", "c", "d")).Should().Be(":4\r\n");
        (await Run("LRANGE", "l", "0", "-1")).Should().Be(BulkArray("a", "b", "c", "d"));
    }

    [Fact]
    public async Task Lindex_and_lset()
    {
        await Run("RPUSH", "l", "a", "b", "c");

        (await Run("LINDEX", "l", "-1")).Should().Be(Bulk("c"));
        (await Run("LINDEX", "l", "9")).Should().Be("$-1\r\n");
        (await Run("LSET", "l", "1", "B")).Should().Be("+OK\r\n");
        (await Run("LSET", "l", "9", "x")).Should().Be("-ERR index out of range\r\n");
        (await Run("LSET", "missing", "0", "x")).Should().Be("-ERR no such key\r\n");
        (await Run("LRANGE", "l", "0", "-1")).Should().Be(BulkArray("a", "B", "c"));
    }

    [Fact]
    public async Task Linsert_before_and_after()
    {
        await Run("RPUSH", "l", "a", "c");

        (await Run("LINSERT", "l", "before", "c", "b")).Should().Be(":3\r\n");
        (await Run("LINSERT", "l", "AFTER", "c", "d")).Should().Be(":4\r\n");
        (await Run("LINSERT", "l", "BEFORE", "zzz", "x")).Should().Be(":-1\r\n");
        (await Run("LINSERT", "missing", "BEFORE", "a", "x")).Should().Be(":0\r\n");
        (await Run("LINSERT", "l", "BESIDE", "a", "x")).Should().Be("-ERR syntax error\r\n");
        (await Run("LRANGE", "l", "0", "-1")).Should().Be(BulkArray("a", "b", "c", "d"));
    }

    [Fact]
    public async Task Lrem_and_ltrim()
    {
        await Run("RPUSH", "l", "x", "a", "x", "b", "x");

        (await Run("LREM", "l", "-1", "x")).Should().Be(":1\r\n");
        (await Run("LTRIM", "l", "1", "-1")).Should().Be("+OK\r\n");
        (await Run("LRANGE", "l", "0", "-1")).Should().Be(BulkArray("a", "x", "b"));
        (await Run("LREM", "l", "0", "nope")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Lpos_without_count_is_a_single_index_or_null()
    {
        await Run("RPUSH", "l", "a", "b", "c", "b");

        (await Run("LPOS", "l", "b")).Should().Be(":1\r\n");
        (await Run("LPOS", "l", "b", "RANK", "-1")).Should().Be(":3\r\n");
        (await Run("LPOS", "l", "z")).Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Lpos_with_count_is_an_array()
    {
        await Run("RPUSH", "l", "b", "a", "b", "b");

        (await Run("LPOS", "l", "b", "COUNT", "0")).Should().Be("*3\r\n:0\r\n:2\r\n:3\r\n");
        (await Run("LPOS", "l", "b", "COUNT", "2", "MAXLEN", "1")).Should().Be("*1\r\n:0\r\n");
        (await Run("LPOS", "l", "z", "COUNT", "1")).Should().Be("*0\r\n");
    }

    [Theory]
    [InlineData("RANK", "0", "-ERR RANK can't be zero")]
    [InlineData("COUNT", "-1", "-ERR COUNT can't be negative")]
    [InlineData("MAXLEN", "-1", "-ERR MAXLEN can't be negative")]
    [InlineData("BOGUS", "1", "-ERR syntax error")]
    public async Task Lpos_rejects_bad_options(string option, string value, string error)
    {
        await Run("RPUSH", "l", "a");

        (await Run("LPOS", "l", "a", option, value)).Should().StartWith(error);
    }

    [Fact]
    public async Task Lmove_and_rpoplpush()
    {
        await Run("RPUSH", "src", "a", "b", "c");

        (await Run("LMOVE", "src", "dst", "LEFT", "RIGHT")).Should().Be(Bulk("a"));
        (await Run("RPOPLPUSH", "src", "dst")).Should().Be(Bulk("c"));
        (await Run("LMOVE", "missing", "dst", "LEFT", "LEFT")).Should().Be("$-1\r\n");
        (await Run("LMOVE", "src", "dst", "UP", "LEFT")).Should().Be("-ERR syntax error\r\n");

        (await Run("LRANGE", "dst", "0", "-1")).Should().Be(BulkArray("c", "a"));
    }

    [Fact]
    public async Task Rpoplpush_is_replicated_as_lmove()
    {
        await Run("RPUSH", "src", "a");

        Reply reply = await H.Dispatch("RPOPLPUSH", "src", "dst");

        reply.PropagateAs!.ToString().Should().Be("LMOVE src dst RIGHT LEFT");
    }

    [Fact]
    public async Task Writes_that_change_nothing_are_not_replicated()
    {
        await Run("RPUSH", "l", "a");
        long before = H.Master.Offset;

        await Run("LREM", "l", "0", "zzz");
        await Run("LTRIM", "l", "0", "-1");
        await Run("LINSERT", "l", "BEFORE", "zzz", "x");
        await Run("RPUSHX", "missing", "x");
        await Run("LMOVE", "missing", "l", "LEFT", "LEFT");

        H.Master.Offset.Should().Be(before);
    }

    [Fact]
    public async Task Lmove_wakes_a_client_blocked_on_the_destination()
    {
        await Run("RPUSH", "src", "a");
        Task<Reply> blocked = H.Dispatch(H.NewSession(), "BLPOP", "dst", "0");
        while (H.Get<Cinnabar.Server.Blocking.KeyNotifier>().WatchedKeyCount == 0)
        {
            await Task.Delay(5);
        }

        await Run("LMOVE", "src", "dst", "LEFT", "LEFT");

        (await blocked.WaitAsync(TimeSpan.FromSeconds(10))).ToString().Should().Be(BulkArray("dst", "a"));
    }
}
