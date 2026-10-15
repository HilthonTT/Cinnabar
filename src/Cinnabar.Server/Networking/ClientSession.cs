using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Cinnabar.Server.Commands;

namespace Cinnabar.Server.Networking;

/// <summary>
/// Per-connection state: authentication, MULTI queue, pub/sub subscriptions, and a
/// serialised write path to the socket.
/// </summary>
/// <remarks>
/// Several producers write to one socket: command replies, pub/sub pushes, and (for replica
/// connections) the replication stream. All writes go through <see cref="SendAsync"/>, which
/// serialises them so frames never interleave.
/// </remarks>
internal sealed class ClientSession : IAsyncDisposable
{
    private static long s_nextId;

    private readonly Socket? _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _closed = new();
    private readonly Lock _pushLock = new();
    private readonly List<Command> _queuedCommands = [];
    private Channel<byte[]>? _pushChannel;
    private Task? _pushLoop;
    private int _disposed;

    /// <summary>Creates a session for an accepted socket.</summary>
    public ClientSession(Socket? socket, bool requiresAuthentication)
    {
        _socket = socket;
        Id = Interlocked.Increment(ref s_nextId);
        IsAuthenticated = !requiresAuthentication;
        RemoteAddress = (socket?.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "local";
    }

    /// <summary>
    /// Creates the internal session that applies the replication stream on a replica. It has
    /// no socket, is always authenticated, and may write even though the server is read-only.
    /// </summary>
    public static ClientSession ForReplicationLink() => new(socket: null, requiresAuthentication: false)
    {
        IsReplicationLink = true,
    };

    /// <summary>Unique, increasing session ID.</summary>
    public long Id { get; }

    /// <summary>Client IP address, used for per-IP limits.</summary>
    public string RemoteAddress { get; }

    /// <summary>Optional name set with <c>CLIENT SETNAME</c>.</summary>
    public string? Name { get; set; }

    /// <summary>Whether AUTH succeeded (or no password is configured).</summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>This session applies commands received from our master.</summary>
    public bool IsReplicationLink { get; private init; }

    /// <summary>The peer is a replica that completed PSYNC with us.</summary>
    public bool IsReplica { get; set; }

    /// <summary>Replication offset right after this client's latest write; WAIT waits for it.</summary>
    public long LastWriteOffset { get; set; }

    /// <summary>Cancelled when the session is closed.</summary>
    public CancellationToken Closed => _closed.Token;

    // ───────────────────────── Transactions ─────────────────────────

    /// <summary>Inside MULTI.</summary>
    public bool InTransaction { get; private set; }

    /// <summary>Set when a command was rejected while queueing; EXEC then aborts.</summary>
    public bool TransactionFailed { get; set; }

    /// <summary>EXEC is running the queued commands; blocking commands must not block.</summary>
    public bool IsExecuting { get; set; }

    /// <summary>Starts a transaction.</summary>
    public void BeginTransaction()
    {
        InTransaction = true;
        TransactionFailed = false;
        _queuedCommands.Clear();
    }

    /// <summary>Queues a command for EXEC.</summary>
    public void Enqueue(Command command) => _queuedCommands.Add(command);

    /// <summary>Ends the transaction and returns the queued commands.</summary>
    public IReadOnlyList<Command> EndTransaction()
    {
        Command[] commands = [.. _queuedCommands];
        _queuedCommands.Clear();
        InTransaction = false;
        TransactionFailed = false;
        return commands;
    }

    // ───────────────────────── Pub/Sub ─────────────────────────

    /// <summary>Channels this session is subscribed to.</summary>
    public HashSet<string> Channels { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// In subscribed mode only (UN)SUBSCRIBE, PING and QUIT are allowed. Mode ends when the
    /// last channel is unsubscribed.
    /// </summary>
    public bool InSubscribedMode => Channels.Count > 0;

    /// <summary>
    /// Queues a pushed message (pub/sub) without blocking the publisher. A background loop
    /// writes queued pushes to the socket in order.
    /// </summary>
    /// <returns><see langword="false"/> if the session is closed.</returns>
    public bool TryPush(byte[] payload)
    {
        if (_socket is null || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        lock (_pushLock)
        {
            if (_pushChannel is null)
            {
                _pushChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
                _pushLoop = Task.Run(() => PushLoopAsync(_pushChannel.Reader));
            }

            return _pushChannel.Writer.TryWrite(payload);
        }
    }

    private async Task PushLoopAsync(ChannelReader<byte[]> reader)
    {
        try
        {
            await foreach (byte[] payload in reader.ReadAllAsync(Closed))
            {
                await SendAsync(payload, Closed);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Session closed.
        }
    }

    // ───────────────────────── I/O ─────────────────────────

    /// <summary>Writes bytes to the socket. Safe to call from several threads.</summary>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_socket is null || data.IsEmpty)
        {
            return;
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            while (!data.IsEmpty)
            {
                int sent = await _socket.SendAsync(data, SocketFlags.None, cancellationToken);
                data = data[sent..];
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Returns a token that is cancelled when the peer disconnects. Blocking commands use it
    /// so a client that hangs up while waiting does not leave a waiter behind.
    /// </summary>
    /// <remarks>
    /// The connection loop does not read while a command runs, so disconnection is detected by
    /// polling the socket: readable with zero bytes available means the peer closed it.
    /// </remarks>
    public DisconnectWatch WatchForDisconnect(CancellationToken cancellationToken) =>
        new(this, cancellationToken);

    private bool IsPeerClosed()
    {
        try
        {
            return _socket is not null && _socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0;
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            return true;
        }
    }

    /// <summary>Closes the socket and stops background work. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _closed.CancelAsync();

        lock (_pushLock)
        {
            _pushChannel?.Writer.TryComplete();
        }

        if (_pushLoop is not null)
        {
            await _pushLoop;
        }

        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // Already closed by the peer.
        }

        _socket?.Dispose();
        _closed.Dispose();
    }

    /// <summary>A cancellation scope that fires when the client disconnects.</summary>
    public sealed class DisconnectWatch : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly ITimer? _timer;

        internal DisconnectWatch(ClientSession session, CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Closed);
            if (session._socket is not null)
            {
                _timer = TimeProvider.System.CreateTimer(
                    _ =>
                    {
                        try
                        {
                            if (session.IsPeerClosed())
                            {
                                _cts.Cancel();
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // The watch was disposed while the timer fired.
                        }
                    },
                    null,
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromMilliseconds(100));
            }
        }

        /// <summary>Cancelled when the client disconnects or the outer token is cancelled.</summary>
        public CancellationToken Token => _cts.Token;

        /// <inheritdoc />
        public void Dispose()
        {
            _timer?.Dispose();
            _cts.Dispose();
        }
    }
}
