using Cinnabar.Server.Replication;

namespace Cinnabar.Server.Tests.Replication;

public sealed class WriteBarrierTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(100);

    private readonly WriteBarrier _barrier = new();

    [Fact]
    public async Task Shared_holders_enter_concurrently()
    {
        WriteBarrier.Releaser first = await _barrier.EnterSharedAsync();
        ValueTask<WriteBarrier.Releaser> second = _barrier.EnterSharedAsync();

        second.IsCompleted.Should().BeTrue();

        first.Dispose();
        (await second).Dispose();
    }

    [Fact]
    public async Task Exclusive_runs_immediately_when_nobody_holds_the_barrier()
    {
        int result = await _barrier.RunExclusiveAsync(() => 42).WaitAsync(WaitLimit);

        result.Should().Be(42);
    }

    [Fact]
    public async Task Exclusive_waits_for_shared_holders_to_leave()
    {
        WriteBarrier.Releaser a = await _barrier.EnterSharedAsync();
        WriteBarrier.Releaser b = await _barrier.EnterSharedAsync();
        bool ran = false;

        Task<bool> exclusive = _barrier.RunExclusiveAsync(() => ran = true);

        await Task.Delay(ShortWait);
        ran.Should().BeFalse();

        a.Dispose();
        await Task.Delay(ShortWait);
        ran.Should().BeFalse("one shared holder is still inside");

        b.Dispose();
        (await exclusive.WaitAsync(WaitLimit)).Should().BeTrue();
    }

    [Fact]
    public async Task New_shared_entries_wait_while_exclusive_is_pending()
    {
        WriteBarrier.Releaser holder = await _barrier.EnterSharedAsync();
        Task<int> exclusive = _barrier.RunExclusiveAsync(() => 1);

        ValueTask<WriteBarrier.Releaser> late = _barrier.EnterSharedAsync();
        Task<WriteBarrier.Releaser> lateTask = late.AsTask();
        await Task.Delay(ShortWait);
        lateTask.IsCompleted.Should().BeFalse("the exclusive section is waiting to run");

        holder.Dispose();
        await exclusive.WaitAsync(WaitLimit);

        (await lateTask.WaitAsync(WaitLimit)).Dispose();
    }

    [Fact]
    public async Task Shared_entry_is_blocked_while_exclusive_action_runs()
    {
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<int> exclusive = Task.Run(() => _barrier.RunExclusiveAsync(() =>
        {
            inside.Set();
            release.Wait(WaitLimit);
            return 1;
        }));
        inside.Wait(WaitLimit).Should().BeTrue();

        Task<WriteBarrier.Releaser> shared = _barrier.EnterSharedAsync().AsTask();
        await Task.Delay(ShortWait);
        shared.IsCompleted.Should().BeFalse();

        release.Set();
        await exclusive.WaitAsync(WaitLimit);
        (await shared.WaitAsync(WaitLimit)).Dispose();
    }

    [Fact]
    public async Task Exclusive_sections_run_one_at_a_time()
    {
        int inside = 0;
        int maxInside = 0;

        Task[] tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => _barrier.RunExclusiveAsync(() =>
        {
            int now = Interlocked.Increment(ref inside);
            maxInside = Math.Max(maxInside, now);
            Thread.Sleep(5);
            Interlocked.Decrement(ref inside);
            return 0;
        }))).ToArray();

        await Task.WhenAll(tasks).WaitAsync(WaitLimit);
        maxInside.Should().Be(1);
    }

    [Fact]
    public async Task Cancelled_exclusive_releases_the_gate()
    {
        WriteBarrier.Releaser holder = await _barrier.EnterSharedAsync();
        using var cts = new CancellationTokenSource();
        Task<int> exclusive = _barrier.RunExclusiveAsync(() => 1, cts.Token);
        Task<WriteBarrier.Releaser> waitingShared = _barrier.EnterSharedAsync().AsTask();

        await cts.CancelAsync();

        Func<Task> act = () => exclusive;
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await waitingShared.WaitAsync(WaitLimit)).Dispose();
        holder.Dispose();

        (await _barrier.RunExclusiveAsync(() => 2).WaitAsync(WaitLimit)).Should().Be(2);
    }

    [Fact]
    public async Task Cancelled_shared_wait_throws()
    {
        WriteBarrier.Releaser holder = await _barrier.EnterSharedAsync();
        Task<int> exclusive = _barrier.RunExclusiveAsync(() => 1);
        using var cts = new CancellationTokenSource();
        Task<WriteBarrier.Releaser> shared = _barrier.EnterSharedAsync(cts.Token).AsTask();

        await cts.CancelAsync();

        Func<Task> act = () => shared;
        await act.Should().ThrowAsync<OperationCanceledException>();

        holder.Dispose();
        (await exclusive.WaitAsync(WaitLimit)).Should().Be(1);
    }

    [Fact]
    public async Task Exception_in_exclusive_action_releases_the_gate()
    {
        Func<Task> act = () => _barrier.RunExclusiveAsync<int>(() => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        Task<WriteBarrier.Releaser> shared = _barrier.EnterSharedAsync().AsTask();
        (await shared.WaitAsync(WaitLimit)).Dispose();
    }
}
