using Cinnabar.Server.Networking;
using Cinnabar.Server.Persistence;

namespace Cinnabar.Server.Commands.Server;

/// <summary><c>LASTSAVE</c>: Unix time of the last successful save, 0 if none.</summary>
[Command("LASTSAVE", MaxArgs = 0, Group = "server")]
internal sealed class LastSaveHandler(SnapshotService snapshots) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(snapshots.LastSave?.ToUnixTimeSeconds() ?? 0));
}
