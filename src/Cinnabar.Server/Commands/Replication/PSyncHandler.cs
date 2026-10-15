using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;

namespace Cinnabar.Server.Commands.Replication;

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
