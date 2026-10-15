using Cinnabar.Server.Networking;
using Cinnabar.Server.Persistence;

namespace Cinnabar.Server.Commands.Server;

/// <summary><c>SAVE</c>: writes the snapshot synchronously.</summary>
[Command("SAVE", MaxArgs = 0, Group = "server")]
internal sealed class SaveHandler(SnapshotService snapshots) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        await snapshots.SaveAsync(cancellationToken);
        return Reply.Ok;
    }
}
