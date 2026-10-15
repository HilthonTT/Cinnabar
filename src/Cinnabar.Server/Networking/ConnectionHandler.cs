using System.IO.Pipelines;
using System.Net.Sockets;
using Cinnabar.Protocol;
using Cinnabar.Server.Commands;
using Cinnabar.Server.Diagnostics;
using Cinnabar.Server.Security;
using Microsoft.Extensions.Logging;

namespace Cinnabar.Server.Networking;

/// <summary>
/// Runs one client connection: read a command, dispatch it, write the reply, repeat.
/// Pipelined commands are handled one after another, in order.
/// </summary>
internal sealed class ConnectionHandler(
    CommandDispatcher dispatcher,
    IpGuard ipGuard,
    ServerMetrics metrics,
    ILogger<ConnectionHandler> logger)
{
    public async Task RunAsync(ClientSession session, Socket socket, CancellationToken cancellationToken)
    {
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Closed);
        CancellationToken token = linked.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                RespValue? value;
                try
                {
                    value = await RespReader.ReadAsync(reader, RespReaderOptions.Server, token);
                }
                catch (RespProtocolException e)
                {
                    // The stream can't be resynchronised after malformed input; report and close.
                    logger.LogDebug("Client {SessionId} sent malformed RESP: {Reason}", session.Id, e.Message);
                    await session.SendAsync(RespEncoder.Error($"ERR Protocol error: {e.Message}"), token);
                    return;
                }

                if (value is null)
                {
                    return; // Clean disconnect.
                }

                if (!Command.TryCreate(value, out Command? command))
                {
                    if (value.Type == RespType.Array && value.Elements is { Count: 0 })
                    {
                        continue; // Empty inline line.
                    }

                    await session.SendAsync(RespEncoder.Error("ERR Protocol error: expected an array of bulk strings"), token);
                    continue;
                }

                if (!ipGuard.TryConsumeCommand(session.RemoteAddress))
                {
                    metrics.RateLimitedTotal.Inc();
                    await session.SendAsync(RespEncoder.Error("ERR max number of commands per second exceeded"), token);
                    continue;
                }

                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("[{SessionId}] {Command}", session.Id, command);
                }

                Reply reply = await dispatcher.DispatchAsync(command, session, token);
                await session.SendAsync(reply.Bytes, token);

                if (reply.CloseConnection)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Server shutting down or session closed.
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            logger.LogDebug("Client {SessionId} connection dropped: {Reason}", session.Id, e.Message);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }
}
