using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;

namespace Cinnabar.Server.Commands.Replication;

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
