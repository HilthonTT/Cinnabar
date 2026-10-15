using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Cinnabar.Protocol;
using Cinnabar.Server.Commands;
using Cinnabar.Server.Diagnostics;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;
using Cinnabar.Storage.Persistence;
using Microsoft.Extensions.Logging;

namespace Cinnabar.Server.Replication;

/// <summary>
/// The master side of replication: full resynchronisation (PSYNC), streaming write commands
/// to replicas, and tracking acknowledgements for WAIT.
/// </summary>
/// <remarks>
/// The offset counts the bytes of every command ever propagated. A replica reports, with
/// <c>REPLCONF ACK</c>, the offset it has applied; WAIT compares that with the offset
/// recorded after the waiting client's last write.
/// </remarks>
internal sealed class ReplicationMaster(Keyspace keyspace, ServerMetrics metrics, ILogger<ReplicationMaster> logger)
{
    private readonly ConcurrentDictionary<long, ConnectedReplica> _replicas = new();
    private readonly Lock _propagateLock = new();
    private long _offset;

    /// <summary>Random 40-character ID of this master's replication history.</summary>
    public string ReplicationId { get; } = RandomNumberGenerator.GetHexString(40, lowercase: true);

    /// <summary>Bytes propagated so far.</summary>
    public long Offset => Interlocked.Read(ref _offset);

    /// <summary>Gate between writes and snapshots; see <see cref="WriteBarrier"/>.</summary>
    public WriteBarrier Barrier { get; } = new();

    /// <summary>Connected replicas.</summary>
    public IReadOnlyCollection<ConnectedReplica> Replicas => [.. _replicas.Values];

    /// <summary>
    /// Sends a write command to every replica.
    /// </summary>
    /// <returns>The replication offset just after this command.</returns>
    public long Propagate(Command command)
    {
        byte[] payload = command.Encode();

        // One lock keeps offset order and per-replica queue order identical.
        lock (_propagateLock)
        {
            long offset = Interlocked.Add(ref _offset, payload.Length);
            foreach (ConnectedReplica replica in _replicas.Values)
            {
                replica.Enqueue(payload);
            }

            metrics.ReplicationOffset.Set(offset);
            return offset;
        }
    }

    /// <summary>
    /// Handles <c>PSYNC</c>: sends <c>+FULLRESYNC &lt;id&gt; &lt;offset&gt;</c> and an RDB
    /// snapshot, then streams every later write.
    /// </summary>
    public async Task StartFullSyncAsync(ClientSession session, CancellationToken cancellationToken)
    {
        (IReadOnlyList<KeySnapshot> keys, long offset, ConnectedReplica replica) = await Barrier.RunExclusiveAsync(
            () =>
            {
                lock (_propagateLock)
                {
                    var newReplica = new ConnectedReplica(session, _offset, logger);
                    _replicas[session.Id] = newReplica;
                    return (keyspace.Snapshot(), _offset, newReplica);
                }
            },
            cancellationToken);

        session.IsReplica = true;
        metrics.ConnectedReplicas.Set(_replicas.Count);

        byte[] rdb = RdbWriter.Write(keys, out RdbWriteResult result);
        if (result.SkippedKeys > 0)
        {
            logger.LogWarning(
                "Full sync to replica {SessionId} skipped {Count} stream key(s); streams are not included in RDB snapshots.",
                session.Id,
                result.SkippedKeys);
        }

        await session.SendAsync(RespEncoder.SimpleString($"FULLRESYNC {ReplicationId} {offset}"), cancellationToken);

        // The RDB transfer is a bulk string without the trailing CRLF.
        await session.SendAsync(Encoding.ASCII.GetBytes($"${rdb.Length}\r\n"), cancellationToken);
        await session.SendAsync(rdb, cancellationToken);

        replica.Start();
        logger.LogInformation(
            "Replica {SessionId} ({Address}) synchronised at offset {Offset} with {Keys} key(s).",
            session.Id,
            session.RemoteAddress,
            offset,
            result.WrittenKeys);
    }

    /// <summary>Records <c>REPLCONF ACK &lt;offset&gt;</c> from a replica.</summary>
    public void Acknowledge(ClientSession session, long offset)
    {
        if (_replicas.TryGetValue(session.Id, out ConnectedReplica? replica))
        {
            replica.Acknowledge(offset);
        }
    }

    /// <summary>Forgets a disconnected replica.</summary>
    public void RemoveReplica(ClientSession session)
    {
        if (_replicas.TryRemove(session.Id, out ConnectedReplica? replica))
        {
            replica.Stop();
            metrics.ConnectedReplicas.Set(_replicas.Count);
            logger.LogInformation("Replica {SessionId} disconnected.", session.Id);
        }
    }

    /// <summary>Number of replicas that have acknowledged at least <paramref name="offset"/>.</summary>
    public int CountAcknowledged(long offset) => _replicas.Values.Count(r => r.AckOffset >= offset);

    /// <summary>
    /// Implements WAIT: asks replicas for their offset and waits until
    /// <paramref name="replicaCount"/> of them have reached <paramref name="offset"/>.
    /// </summary>
    /// <param name="replicaCount">Number of replicas to wait for.</param>
    /// <param name="offset">Offset that counts as caught up.</param>
    /// <param name="timeout">How long to wait; <see langword="null"/> waits forever.</param>
    /// <param name="cancellationToken">Cancels the wait (for example when the client disconnects).</param>
    /// <returns>How many replicas reached the offset.</returns>
    public async Task<int> WaitForReplicasAsync(
        int replicaCount,
        long offset,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        int acknowledged = CountAcknowledged(offset);
        if (acknowledged >= replicaCount || _replicas.IsEmpty)
        {
            return acknowledged;
        }

        // GETACK travels through the replication stream, so a replica answers it only
        // after applying everything sent before it.
        Propagate(new Command("REPLCONF", "GETACK", "*"));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } limit)
        {
            timeoutCts.CancelAfter(limit);
        }

        try
        {
            while (acknowledged < replicaCount)
            {
                await Task.Delay(10, timeoutCts.Token);
                acknowledged = CountAcknowledged(offset);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out: report how many made it.
        }

        return CountAcknowledged(offset);
    }
}
