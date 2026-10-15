namespace Cinnabar.Server.Blocking;

/// <summary>
/// Wakes blocked commands (BLPOP, XREAD BLOCK) when a key they wait on changes.
/// </summary>
/// <remarks>
/// Use it in this order to avoid lost wake-ups:
/// <list type="number">
///   <item><see cref="Register"/> the keys,</item>
///   <item>check for data,</item>
///   <item>await <see cref="KeyWaiter.Signaled"/> only if there was none.</item>
/// </list>
/// A push that lands between steps 1 and 2 is seen in step 2; one after step 2 completes the waiter.
/// Every waiter on a key is woken; whoever pops first wins, and the rest register again.
/// </remarks>
internal sealed class KeyNotifier
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<KeyWaiter>> _waiters = new(StringComparer.Ordinal);

    /// <summary>Number of keys with at least one waiter (for diagnostics and tests).</summary>
    public int WatchedKeyCount
    {
        get
        {
            lock (_lock)
            {
                return _waiters.Count;
            }
        }
    }

    /// <summary>Starts watching <paramref name="keys"/>. Dispose the waiter to stop.</summary>
    public KeyWaiter Register(IReadOnlyCollection<string> keys)
    {
        var waiter = new KeyWaiter(this, keys);
        lock (_lock)
        {
            foreach (string key in keys)
            {
                if (!_waiters.TryGetValue(key, out HashSet<KeyWaiter>? set))
                {
                    set = [];
                    _waiters[key] = set;
                }

                set.Add(waiter);
            }
        }

        return waiter;
    }

    /// <summary>Wakes every waiter on <paramref name="key"/>.</summary>
    public void Notify(string key)
    {
        KeyWaiter[] toWake;
        lock (_lock)
        {
            if (!_waiters.TryGetValue(key, out HashSet<KeyWaiter>? set))
            {
                return;
            }

            toWake = [.. set];
        }

        foreach (KeyWaiter waiter in toWake)
        {
            waiter.Signal();
        }
    }

    private void Unregister(KeyWaiter waiter)
    {
        lock (_lock)
        {
            foreach (string key in waiter.Keys)
            {
                if (_waiters.TryGetValue(key, out HashSet<KeyWaiter>? set) && set.Remove(waiter) && set.Count == 0)
                {
                    _waiters.Remove(key);
                }
            }
        }
    }

    /// <summary>A registration created by <see cref="Register"/>.</summary>
    internal sealed class KeyWaiter : IDisposable
    {
        private readonly KeyNotifier _owner;
        private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal KeyWaiter(KeyNotifier owner, IReadOnlyCollection<string> keys)
        {
            _owner = owner;
            Keys = keys;
        }

        /// <summary>The keys being watched.</summary>
        public IReadOnlyCollection<string> Keys { get; }

        /// <summary>Completes when any watched key changes.</summary>
        public Task Signaled => _signal.Task;

        internal void Signal() => _signal.TrySetResult();

        /// <inheritdoc />
        public void Dispose() => _owner.Unregister(this);
    }
}
