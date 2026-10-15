using System.Net.Sockets;
using System.Threading.Channels;
using Cinnabar.Server.Networking;
using Microsoft.Extensions.Logging;

namespace Cinnabar.Server.Replication;

/// <summary>
/// A replica connected to this master. Propagated commands are queued per replica and sent
/// in order by a background loop, so a slow replica never blocks writers.
/// </summary>
internal sealed class ConnectedReplica(ClientSession session, long startOffset, ILogger logger)
{
    private readonly Channel<byte[]> _queue =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    private long _ackOffset = startOffset;
    private Task? _sendLoop;

    /// <summary>The replica's connection.</summary>
    public ClientSession Session { get; } = session;

    /// <summary>The master offset at which this replica's snapshot was taken.</summary>
    public long StartOffset { get; } = startOffset;

    /// <summary>The highest offset the replica has confirmed with <c>REPLCONF ACK</c>.</summary>
    public long AckOffset => Interlocked.Read(ref _ackOffset);

    /// <summary>Records an acknowledgement; offsets only move forward.</summary>
    public void Acknowledge(long offset)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _ackOffset);
            if (offset <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _ackOffset, offset, current) != current);
    }

    /// <summary>Queues encoded commands for this replica.</summary>
    public void Enqueue(byte[] payload) => _queue.Writer.TryWrite(payload);

    /// <summary>Starts sending queued commands. Call after the snapshot has been sent.</summary>
    public void Start() => _sendLoop ??= Task.Run(SendLoopAsync);

    /// <summary>Stops sending.</summary>
    public void Stop() => _queue.Writer.TryComplete();

    private async Task SendLoopAsync()
    {
        try
        {
            await foreach (byte[] payload in _queue.Reader.ReadAllAsync(Session.Closed))
            {
                await Session.SendAsync(payload, Session.Closed);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            logger.LogInformation("Stopped streaming to replica {SessionId}: {Reason}", Session.Id, e.Message);
        }
    }
}
