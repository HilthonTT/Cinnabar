using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;

namespace Cinnabar.Server.Commands.Replication;

/// <summary>
/// <c>PSYNC replicationid offset</c>: always performs a full resync (partial resync from a
/// backlog is not implemented). The snapshot and command stream are written directly to the
/// connection, so the dispatcher sends nothing.
/// </summary>
/// <remarks>
/// Sending the snapshot can take a while, so PSYNC does not hold the transaction gate; a
/// snapshot taken mid-EXEC is completed by the transaction's remaining writes, which follow
/// in the command stream.
/// </remarks>
[Command("PSYNC", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.NoGate, Group = "replication", Syntax = "replicationid offset")]
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
