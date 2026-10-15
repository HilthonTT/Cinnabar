namespace Cinnabar.Server.Replication;

/// <summary>
/// An async reader/writer gate between write commands (shared) and replica snapshots
/// (exclusive).
/// </summary>
/// <remarks>
/// A full resync needs the snapshot and the replication offset to describe the same moment.
/// Every write command holds the barrier in shared mode from execution until it has been
/// propagated; taking a snapshot waits for in-flight writes to drain and briefly holds off
/// new ones. Without this, a write could be both in the snapshot and replayed afterwards,
/// or in neither.
/// </remarks>
internal sealed class WriteBarrier
{
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _exclusiveTurn = new(1, 1);
    private int _activeShared;
    private bool _exclusivePending;
    private TaskCompletionSource _gateOpen = CompletedSource();
    private TaskCompletionSource _drained = CompletedSource();

    /// <summary>Enters in shared mode. Dispose the result to leave.</summary>
    public async ValueTask<Releaser> EnterSharedAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task wait;
            lock (_lock)
            {
                if (!_exclusivePending)
                {
                    _activeShared++;
                    return new Releaser(this);
                }

                wait = _gateOpen.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Runs <paramref name="action"/> while no shared holder is inside.</summary>
    public async Task<T> RunExclusiveAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        await _exclusiveTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Task drained;
            lock (_lock)
            {
                _exclusivePending = true;
                _gateOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (_activeShared == 0)
                {
                    _drained.TrySetResult();
                }

                drained = _drained.Task;
            }

            try
            {
                await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
                return action();
            }
            finally
            {
                lock (_lock)
                {
                    _exclusivePending = false;
                    _gateOpen.TrySetResult();
                }
            }
        }
        finally
        {
            _exclusiveTurn.Release();
        }
    }

    private void ExitShared()
    {
        lock (_lock)
        {
            _activeShared--;
            if (_activeShared == 0 && _exclusivePending)
            {
                _drained.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    /// <summary>Leaves shared mode when disposed.</summary>
    internal readonly struct Releaser(WriteBarrier barrier) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => barrier.ExitShared();
    }
}
