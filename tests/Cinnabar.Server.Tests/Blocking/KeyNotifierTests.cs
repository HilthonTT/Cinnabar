using Cinnabar.Server.Blocking;

namespace Cinnabar.Server.Tests.Blocking;

public sealed class KeyNotifierTests
{
    private readonly KeyNotifier _notifier = new();

    [Fact]
    public void New_waiter_is_not_signaled()
    {
        using KeyNotifier.KeyWaiter waiter = _notifier.Register(["a"]);

        waiter.Signaled.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Notify_after_register_signals_waiter()
    {
        // Register first, then check for data, then wait: a notification that arrives after
        // registering is never lost.
        using KeyNotifier.KeyWaiter waiter = _notifier.Register(["a"]);

        _notifier.Notify("a");

        waiter.Signaled.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Notify_before_register_is_not_remembered()
    {
        _notifier.Notify("a");

        using KeyNotifier.KeyWaiter waiter = _notifier.Register(["a"]);

        waiter.Signaled.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Notify_other_key_does_not_signal()
    {
        using KeyNotifier.KeyWaiter waiter = _notifier.Register(["a"]);

        _notifier.Notify("b");

        waiter.Signaled.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Notify_wakes_every_waiter_on_the_key()
    {
        using KeyNotifier.KeyWaiter first = _notifier.Register(["a"]);
        using KeyNotifier.KeyWaiter second = _notifier.Register(["a", "b"]);
        using KeyNotifier.KeyWaiter other = _notifier.Register(["c"]);

        _notifier.Notify("a");

        first.Signaled.IsCompleted.Should().BeTrue();
        second.Signaled.IsCompleted.Should().BeTrue();
        other.Signaled.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Waiter_on_several_keys_is_signaled_by_any()
    {
        using KeyNotifier.KeyWaiter waiter = _notifier.Register(["a", "b"]);

        _notifier.Notify("b");

        waiter.Signaled.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Repeated_notify_is_harmless()
    {
        using KeyNotifier.KeyWaiter waiter = _notifier.Register(["a"]);

        _notifier.Notify("a");
        _notifier.Notify("a");

        waiter.Signaled.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Watched_key_count_tracks_registrations()
    {
        KeyNotifier.KeyWaiter first = _notifier.Register(["a", "b"]);
        KeyNotifier.KeyWaiter second = _notifier.Register(["b", "c"]);

        _notifier.WatchedKeyCount.Should().Be(3);

        first.Dispose();
        _notifier.WatchedKeyCount.Should().Be(2);

        second.Dispose();
        _notifier.WatchedKeyCount.Should().Be(0);
    }

    [Fact]
    public void Disposed_waiter_is_not_signaled()
    {
        KeyNotifier.KeyWaiter waiter = _notifier.Register(["a"]);
        waiter.Dispose();

        _notifier.Notify("a");

        waiter.Signaled.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Dispose_twice_is_safe()
    {
        KeyNotifier.KeyWaiter waiter = _notifier.Register(["a"]);

        waiter.Dispose();
        waiter.Dispose();

        _notifier.WatchedKeyCount.Should().Be(0);
    }

    [Fact]
    public void Keys_are_case_sensitive()
    {
        using KeyNotifier.KeyWaiter waiter = _notifier.Register(["Key"]);

        _notifier.Notify("key");

        waiter.Signaled.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Signaled_task_completes_asynchronously_for_awaiters()
    {
        using KeyNotifier.KeyWaiter waiter = _notifier.Register(["a"]);
        Task wait = waiter.Signaled.WaitAsync(TimeSpan.FromSeconds(5));

        _ = Task.Run(() => _notifier.Notify("a"));

        await wait;
    }
}
