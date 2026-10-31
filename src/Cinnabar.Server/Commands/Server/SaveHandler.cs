using Cinnabar.Server.Networking;
using Cinnabar.Server.Persistence;

namespace Cinnabar.Server.Commands.Server;

/// <summary><c>SAVE</c>: writes the snapshot synchronously.</summary>
[Command("SAVE", MaxArgs = 0, Flags = CommandFlags.NoGate, Group = "server")]
internal sealed class SaveHandler(SnapshotService snapshots) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        // SAVE is ungated so the snapshot can take the transaction gate exclusively; inside
        // EXEC the transaction already holds it exclusively.
        await snapshots.SaveAsync(cancellationToken, callerHoldsGate: session.IsExecuting);
        return Reply.Ok;
    }
}
