using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Cinnabar.Protocol;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Diagnostics;
using Cinnabar.Server.PubSub;
using Cinnabar.Server.Replication;
using Cinnabar.Server.Security;
using Microsoft.Extensions.Logging;

namespace Cinnabar.Server.Networking;

/// <summary>
/// Accepts TCP connections, enforces connection limits, and runs a
/// <see cref="ConnectionHandler"/> per client. Cleans up subscriptions and replica
/// registrations when a client disconnects.
/// </summary>
internal sealed class TcpServer(
    ServerSettings settings,
    ConnectionHandler connectionHandler,
    IpGuard ipGuard,
    PubSubBroker broker,
    ReplicationMaster master,
    ServerMetrics metrics,
    ILogger<TcpServer> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, (ClientSession Session, Task Task)> _connections = new();
    private readonly CancellationTokenSource _stopping = new();
    private int _disposeState;
    private TcpListener? _listener;
    private Task? _acceptLoop;

    /// <summary>The bound endpoint; valid after <see cref="Start"/>.</summary>
    public IPEndPoint LocalEndPoint => (IPEndPoint?)_listener?.LocalEndpoint
        ?? throw new InvalidOperationException("The server has not been started.");

    /// <summary>Binds the listening socket and starts accepting connections.</summary>
    public void Start()
    {
        if (!IPAddress.TryParse(settings.Network.BindAddress, out IPAddress? address))
        {
            throw new ArgumentException($"Invalid bind address '{settings.Network.BindAddress}'.");
        }

        _listener = new TcpListener(address, settings.Network.Port);
        _listener.Start(backlog: 511);
        _acceptLoop = AcceptLoopAsync(_stopping.Token);
        logger.LogInformation("Listening on {EndPoint}.", LocalEndPoint);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener!.AcceptSocketAsync(cancellationToken);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                break;
            }
            catch (SocketException e)
            {
                logger.LogWarning("Accept failed: {Reason}", e.Message);
                continue;
            }

            socket.NoDelay = true;
            _ = HandleClientAsync(socket, cancellationToken);
        }
    }

    private async Task HandleClientAsync(Socket socket, CancellationToken cancellationToken)
    {
        var session = new ClientSession(socket, settings.Security.RequiresAuthentication);

        string? rejection = ipGuard.TryAcceptConnection(session.RemoteAddress);
        if (rejection is not null)
        {
            metrics.ConnectionsRejectedTotal.Inc();
            logger.LogWarning("Rejected connection from {Address}: {Reason}", session.RemoteAddress, rejection);
            try
            {
                await session.SendAsync(RespEncoder.Error(rejection), cancellationToken);
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException)
            {
                // Best effort.
            }

            await session.DisposeAsync();
            return;
        }

        metrics.ConnectionsTotal.Inc();
        metrics.ActiveConnections.Inc();
        logger.LogDebug("Client {SessionId} connected from {Address}.", session.Id, session.RemoteAddress);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _connections[session.Id] = (session, completion.Task);

        try
        {
            await connectionHandler.RunAsync(session, socket, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Client {SessionId} failed.", session.Id);
        }
        finally
        {
            broker.UnsubscribeAll(session);
            master.RemoveReplica(session);
            await session.DisposeAsync();

            ipGuard.ReleaseConnection(session.RemoteAddress);
            metrics.ActiveConnections.Dec();
            _connections.TryRemove(session.Id, out _);
            completion.TrySetResult();
            logger.LogDebug("Client {SessionId} disconnected.", session.Id);
        }
    }

    /// <summary>Stops accepting, closes every connection and waits for them to finish.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync();
        _listener?.Stop();

        if (_acceptLoop is not null)
        {
            await _acceptLoop;
        }

        foreach ((ClientSession session, _) in _connections.Values)
        {
            await session.DisposeAsync();
        }

        await Task.WhenAll(_connections.Values.Select(c => c.Task));
        _stopping.Dispose();
    }
}
