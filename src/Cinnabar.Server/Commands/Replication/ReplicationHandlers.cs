using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;

namespace Cinnabar.Server.Commands.Replication;

/// <summary>
/// <c>REPLCONF listening-port port | capa capability | ACK offset</c>: sent by replicas
/// during the handshake and to acknowledge progress. ACK gets no reply.
/// </summary>
[Command("REPLCONF", MinArgs = 2, Group = "replication", Syntax = "option value [option value ...]")]
internal sealed class ReplConfHandler(ReplicationMaster master) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (Arguments.Is(command[0], "ACK"))
        {
            master.Acknowledge(session, Arguments.ParseLong(command[1]));
            return ValueTask.FromResult(Reply.None);
        }

        return ValueTask.FromResult(Reply.Ok);
    }
}

/// <summary>
/// <c>PSYNC replicationid offset</c>: always performs a full resync (partial resync from a
/// backlog is not implemented). The snapshot and command stream are written directly to the
/// connection, so the dispatcher sends nothing.
/// </summary>
[Command("PSYNC", MinArgs = 2, MaxArgs = 2, Group = "replication", Syntax = "replicationid offset")]
internal sealed class PSyncHandler(ReplicationMaster master, ServerSettings settings) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (settings.Replication.IsReplica)
        {
            return Reply.Error("ERR Chained replication is not supported; connect replicas to the master.");
        }

        await master.StartFullSyncAsync(session, cancellationToken);
        return Reply.None;
    }
}

/// <summary>
/// <c>WAIT numreplicas timeout</c>: blocks until that many replicas have applied this
/// client's writes, or the timeout (milliseconds, 0 = forever) passes. Returns how many did.
/// </summary>
[Command("WAIT", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Blocking, Group = "replication", Syntax = "numreplicas timeout")]
internal sealed class WaitHandler(ReplicationMaster master, ServerSettings settings) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (settings.Replication.IsReplica)
        {
            return Reply.Error("ERR WAIT cannot be used with replica instances.");
        }

        int replicas = Arguments.ParseCount(command[0]);
        TimeSpan? timeout = Arguments.ParseTimeoutMilliseconds(command[1]);

        if (session.IsExecuting)
        {
            return Reply.Integer(master.CountAcknowledged(session.LastWriteOffset));
        }

        using ClientSession.DisconnectWatch watch = session.WatchForDisconnect(cancellationToken);
        try
        {
            return Reply.Integer(await master.WaitForReplicasAsync(replicas, session.LastWriteOffset, timeout, watch.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client disconnected; nobody will read the reply.
            return Reply.None;
        }
    }
}
