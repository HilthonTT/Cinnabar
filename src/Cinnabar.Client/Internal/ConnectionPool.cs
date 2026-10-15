using System.Collections.Concurrent;

namespace Cinnabar.Client.Internal;

/// <summary>
/// A bounded pool of <see cref="RedisConnection"/>s. At most <c>PoolSize</c> connections
/// are leased at once; connections are opened lazily and reused while healthy.
/// </summary>
internal sealed class ConnectionPool(RedisClientOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _slots = new(options.PoolSize, options.PoolSize);
    private readonly ConcurrentBag<RedisConnection> _idle = [];
    private volatile bool _disposed;

    /// <summary>Number of idle connections (for tests).</summary>
    public int IdleCount => _idle.Count;

    /// <summary>
    /// Leases a connection. Dispose the lease to return it; a connection that failed
    /// during use is discarded instead.
    /// </summary>
    /// <exception cref="RedisConnectionException">No connection became free within <c>PoolTimeout</c>, or connecting failed.</exception>
    public async Task<Lease> RentAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!await _slots.WaitAsync(options.PoolTimeout, cancellationToken).ConfigureAwait(false))
        {
            throw new RedisConnectionException(
                $"No pooled connection became available within {options.PoolTimeout.TotalSeconds:0.#}s " +
                $"(PoolSize = {options.PoolSize}). Increase PoolSize or reduce long-running commands.");
        }

        try
        {
            while (_idle.TryTake(out RedisConnection? idle))
            {
                if (idle.IsHealthy)
                {
                    return new Lease(this, idle);
                }

                await idle.DisposeAsync().ConfigureAwait(false);
            }

            return new Lease(this, await RedisConnection.OpenAsync(options, cancellationToken).ConfigureAwait(false));
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    private async ValueTask ReturnAsync(RedisConnection connection)
    {
        if (_disposed || !connection.IsHealthy)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _idle.Add(connection);
        }

        if (!_disposed)
        {
            _slots.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        while (_idle.TryTake(out RedisConnection? connection))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A leased connection; disposing returns it to the pool.</summary>
    internal sealed class Lease(ConnectionPool pool, RedisConnection connection) : IAsyncDisposable
    {
        private int _returned;

        public RedisConnection Connection { get; } = connection;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _returned, 1) == 0 ? pool.ReturnAsync(Connection) : ValueTask.CompletedTask;
    }
}
