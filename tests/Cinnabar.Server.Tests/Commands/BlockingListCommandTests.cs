using Cinnabar.Server.Blocking;
using Cinnabar.Server.Commands;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class BlockingListCommandTests : DispatcherTestBase
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Blpop_pops_immediately_when_data_exists()
    {
        await Run("RPUSH", "l", "a", "b");

        (await Run("BLPOP", "l", "1")).Should().Be(BulkArray("l", "a"));
    }

    [Fact]
    public async Task Brpop_pops_from_tail()
    {
        await Run("RPUSH", "l", "a", "b");

        (await Run("BRPOP", "l", "1")).Should().Be(BulkArray("l", "b"));
    }

    [Fact]
    public async Task Blpop_takes_from_first_non_empty_key()
    {
        await Run("RPUSH", "second", "x");
        await Run("RPUSH", "third", "y");

        (await Run("BLPOP", "first", "second", "third", "1")).Should().Be(BulkArray("second", "x"));
    }

    [Fact]
    public async Task Blpop_times_out_with_null_array()
    {
        (await Run("BLPOP", "empty", "0.1")).Should().Be("*-1\r\n");
        H.Get<KeyNotifier>().WatchedKeyCount.Should().Be(0);
    }

    [Fact]
    public async Task Blpop_wakes_up_when_another_client_pushes()
    {
        Task<Reply> blocked = H.Dispatch("BLPOP", "l", "0");
        await WaitUntilWatching("l");
        blocked.IsCompleted.Should().BeFalse();

        await H.Dispatch(H.NewSession(), "RPUSH", "l", "hello");

        (await blocked.WaitAsync(WaitLimit)).ToString().Should().Be(BulkArray("l", "hello"));
        (await Run("LLEN", "l")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Two_waiters_are_both_served_by_two_pushes()
    {
        ClientSession first = H.NewSession();
        ClientSession second = H.NewSession();
        Task<Reply> a = H.Dispatch(first, "BLPOP", "l", "0");
        Task<Reply> b = H.Dispatch(second, "BLPOP", "l", "0");
        await WaitUntilWatching("l");

        await H.Dispatch(H.NewSession(), "RPUSH", "l", "1");
        await H.Dispatch(H.NewSession(), "RPUSH", "l", "2");

        Reply[] replies = await Task.WhenAll(a, b).WaitAsync(WaitLimit);
        replies.Select(r => r.ToString()).Should().BeEquivalentTo([BulkArray("l", "1"), BulkArray("l", "2")]);
    }

    [Fact]
    public async Task Blpop_with_negative_timeout_is_an_error()
    {
        (await Run("BLPOP", "l", "-1")).Should().Be("-ERR timeout is negative\r\n");
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task Blpop_with_invalid_timeout_is_an_error(string timeout)
    {
        (await Run("BLPOP", "l", timeout)).Should().Be("-ERR timeout is not a float or out of range\r\n");
    }

    [Fact]
    public async Task Blpop_on_string_key_is_wrongtype()
    {
        await Run("SET", "k", "v");

        (await Run("BLPOP", "k", "0.1")).Should().StartWith("-WRONGTYPE");
    }

    [Fact]
    public async Task Blpop_inside_exec_does_not_block()
    {
        await Run("MULTI");
        await Run("BLPOP", "empty", "0");

        Task<string> exec = Run("EXEC");

        (await exec.WaitAsync(WaitLimit)).Should().Be("*1\r\n*-1\r\n");
    }

    [Fact]
    public async Task Blpop_propagates_as_lpop()
    {
        await Run("RPUSH", "l", "a");
        long before = H.Master.Offset;

        Reply reply = await H.Dispatch("BLPOP", "l", "1");

        reply.SkipPropagation.Should().BeTrue();
        H.Master.Offset.Should().Be(before + new Command("LPOP", "l").Encode().Length);
        H.Session.LastWriteOffset.Should().Be(H.Master.Offset);
    }

    [Fact]
    public async Task Brpop_propagates_as_rpop()
    {
        await Run("RPUSH", "list", "a");
        long before = H.Master.Offset;

        await H.Dispatch("BRPOP", "list", "1");

        H.Master.Offset.Should().Be(before + new Command("RPOP", "list").Encode().Length);
    }

    [Fact]
    public async Task Blpop_timeout_does_not_propagate()
    {
        await Run("BLPOP", "empty", "0.05");

        H.Master.Offset.Should().Be(0);
    }

    [Fact]
    public async Task Blpop_is_cancelled_with_its_token()
    {
        using var cts = new CancellationTokenSource();
        ValueTask<Reply> blocked = H.Dispatcher.DispatchAsync(new Command("BLPOP", "l", "0"), H.Session, cts.Token);
        await WaitUntilWatching("l");

        await cts.CancelAsync();

        Func<Task> act = async () => await blocked;
        await act.Should().ThrowAsync<OperationCanceledException>();
        H.Get<KeyNotifier>().WatchedKeyCount.Should().Be(0);
    }

    private async Task WaitUntilWatching(string key)
    {
        KeyNotifier notifier = H.Get<KeyNotifier>();
        DateTime deadline = DateTime.UtcNow + WaitLimit;
        while (notifier.WatchedKeyCount == 0)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, $"a waiter on '{key}' should register");
            await Task.Delay(5);
        }
    }
}
