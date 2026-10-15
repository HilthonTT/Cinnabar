using System.IO.Pipelines;
using System.Net.Sockets;
using Cinnabar.Protocol;

namespace Cinnabar.Client.Internal;

/// <summary>
/// One TCP connection. Not thread-safe: the pool hands each connection to one caller at a time.
/// </summary>
/// <remarks>
/// A connection whose request/reply exchange is interrupted (cancellation, I/O error,
/// malformed reply) may still have an unread reply in flight. It is marked faulted so the
/// pool discards it instead of handing a desynchronised connection to the next caller.
/// </remarks>
internal sealed class RedisConnection : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;
    private bool _faulted;
    private bool _disposed;

    private RedisConnection(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(leaveOpen: true));
    }

    /// <summary>False once the connection is closed or an exchange was interrupted.</summary>
    public bool IsHealthy => !_faulted && !_disposed && _tcp.Connected;

    /// <summary>Opens a connection and authenticates if a password is configured.</summary>
    /// <exception cref="RedisConnectionException">Connecting failed or timed out.</exception>
    /// <exception cref="RedisException">AUTH was rejected.</exception>
    public static async Task<RedisConnection> OpenAsync(RedisClientOptions options, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectTimeout);

        try
        {
            await tcp.ConnectAsync(options.Host, options.Port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            tcp.Dispose();
            throw new RedisConnectionException(
                $"Timed out connecting to {options.Host}:{options.Port} after {options.ConnectTimeout.TotalSeconds:0.#}s.");
        }
        catch (SocketException e)
        {
            tcp.Dispose();
            throw new RedisConnectionException($"Could not connect to {options.Host}:{options.Port}: {e.Message}", e);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        var connection = new RedisConnection(tcp);
        if (!string.IsNullOrEmpty(options.Password))
        {
            try
            {
                RespValue reply = await connection.ExecuteAsync(["AUTH", options.Username, options.Password], cancellationToken)
                    .ConfigureAwait(false);
                reply.ThrowIfError();
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        return connection;
    }

    /// <summary>Sends one command and reads its reply.</summary>
    public async Task<RespValue> ExecuteAsync(IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        IReadOnlyList<RespValue> replies = await ExecuteManyAsync([command], cancellationToken).ConfigureAwait(false);
        return replies[0];
    }

    /// <summary>
    /// Pipelines several commands: writes them all, then reads one reply per command.
    /// </summary>
    public async Task<IReadOnlyList<RespValue>> ExecuteManyAsync(
        IReadOnlyList<IReadOnlyList<string>> commands,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Until every reply has been read, an interruption leaves the stream out of sync.
        _faulted = true;
        try
        {
            foreach (IReadOnlyList<string> command in commands)
            {
                await _stream.WriteAsync(RespEncoder.Command(command), cancellationToken).ConfigureAwait(false);
            }

            var replies = new RespValue[commands.Count];
            for (int i = 0; i < replies.Length; i++)
            {
                replies[i] = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            }

            _faulted = false;
            return replies;
        }
        catch (Exception e) when (e is IOException or SocketException or RespProtocolException)
        {
            throw new RedisConnectionException($"Connection error: {e.Message}", e);
        }
    }

    /// <summary>Sends a command without reading a reply (pub/sub).</summary>
    public async Task SendAsync(IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            await _stream.WriteAsync(RespEncoder.Command(command), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException)
        {
            _faulted = true;
            throw new RedisConnectionException($"Connection error: {e.Message}", e);
        }
    }

    /// <summary>Reads the next value without sending anything (pub/sub pushes).</summary>
    public async Task<RespValue> ReadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or RespProtocolException)
        {
            _faulted = true;
            throw new RedisConnectionException($"Connection error: {e.Message}", e);
        }
    }

    private async Task<RespValue> ReadCoreAsync(CancellationToken cancellationToken) =>
        await RespReader.ReadAsync(_reader, RespReaderOptions.Default, cancellationToken).ConfigureAwait(false)
        ?? throw new IOException("The server closed the connection.");

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _reader.CompleteAsync().ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
        _tcp.Dispose();
    }
}
