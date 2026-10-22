namespace Cinnabar.Server.Concurrency;

/// <summary>
/// An async reader/writer gate: any number of shared holders, or one exclusive holder.
/// </summary>
/// <remarks>
/// Exclusive requests have priority: once one is waiting, new shared holders queue behind it,
/// so a steady stream of shared holders cannot starve it. Shared holders must not wait on
/// anything that needs the exclusive side, and the exclusive holder must not enter shared
/// mode on the same gate.
/// </remarks>
internal class SharedExclusiveGate
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
                    return new Releaser(this, exclusive: false);
                }

                wait = _gateOpen.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until no shared holder is inside, then holds the gate exclusively. Dispose the
    /// result to leave.
    /// </summary>
    public async ValueTask<Releaser> EnterExclusiveAsync(CancellationToken cancellationToken = default)
    {
        await _exclusiveTurn.WaitAsync(cancellationToken).ConfigureAwait(false);

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
        }
        catch
        {
            ExitExclusive();
            throw;
        }

        return new Releaser(this, exclusive: true);
    }

    /// <summary>Runs <paramref name="action"/> while no shared holder is inside.</summary>
    public async Task<T> RunExclusiveAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        using (await EnterExclusiveAsync(cancellationToken).ConfigureAwait(false))
        {
            return action();
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

    private void ExitExclusive()
    {
        lock (_lock)
        {
            _exclusivePending = false;
            _gateOpen.TrySetResult();
        }

        _exclusiveTurn.Release();
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    /// <summary>Leaves the gate when disposed.</summary>
    internal readonly struct Releaser(SharedExclusiveGate gate, bool exclusive) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
            if (exclusive)
            {
                gate.ExitExclusive();
            }
            else
            {
                gate.ExitShared();
            }
        }
    }
}
