using Cinnabar.Server.Blocking;
using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class BlockingMoveCommandTests : DispatcherTestBase
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Blmove_moves_immediately_when_data_exists()
    {
        await Run("RPUSH", "src", "a", "b");

        (await Run("BLMOVE", "src", "dst", "RIGHT", "LEFT", "1")).Should().Be(Bulk("b"));
        (await Run("LRANGE", "dst", "0", "-1")).Should().Be(BulkArray("b"));
    }

    [Fact]
    public async Task Blmove_times_out_with_null_bulk_string()
    {
        (await Run("BLMOVE", "src", "dst", "LEFT", "LEFT", "0.1")).Should().Be("$-1\r\n");
        H.Get<KeyNotifier>().WatchedKeyCount.Should().Be(0);
    }

    [Fact]
    public async Task Brpoplpush_wakes_when_the_source_gets_data()
    {
        Task<Reply> blocked = H.Dispatch("BRPOPLPUSH", "src", "dst", "0");
        await WaitUntilWatching();

        await H.Dispatch(H.NewSession(), "RPUSH", "src", "x");

        (await blocked.WaitAsync(WaitLimit)).ToString().Should().Be(Bulk("x"));
        (await Run("LRANGE", "dst", "0", "-1")).Should().Be(BulkArray("x"));
        (await Run("EXISTS", "src")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Blmove_propagates_lmove_itself()
    {
        await Run("RPUSH", "src", "a");
        long before = H.Master.Offset;

        Reply reply = await H.Dispatch("BLMOVE", "src", "dst", "LEFT", "RIGHT", "1");

        reply.SkipPropagation.Should().BeTrue();
        H.Master.Offset.Should().Be(before + new Command("LMOVE", "src", "dst", "LEFT", "RIGHT").Encode().Length);
        H.Session.LastWriteOffset.Should().Be(H.Master.Offset);
    }

    [Fact]
    public async Task Blmove_inside_exec_does_not_block()
    {
        await Run("MULTI");
        await Run("BLMOVE", "src", "dst", "LEFT", "LEFT", "0");

        (await Run("EXEC").WaitAsync(WaitLimit)).Should().Be("*1\r\n$-1\r\n");
    }

    [Fact]
    public async Task Blmove_rejects_bad_direction()
    {
        (await Run("BLMOVE", "src", "dst", "UP", "LEFT", "0")).Should().Be("-ERR syntax error\r\n");
    }

    private async Task WaitUntilWatching()
    {
        KeyNotifier notifier = H.Get<KeyNotifier>();
        DateTime deadline = DateTime.UtcNow + WaitLimit;
        while (notifier.WatchedKeyCount == 0)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "a waiter should register");
            await Task.Delay(5);
        }
    }
}
