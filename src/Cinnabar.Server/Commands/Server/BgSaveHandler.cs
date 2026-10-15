using Cinnabar.Server.Networking;
using Cinnabar.Server.Persistence;

namespace Cinnabar.Server.Commands.Server;

/// <summary><c>BGSAVE</c>: writes the snapshot in the background.</summary>
[Command("BGSAVE", MaxArgs = 1, Group = "server", Syntax = "[SCHEDULE]")]
internal sealed class BgSaveHandler(SnapshotService snapshots) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(snapshots.TryStartBackgroundSave()
            ? Reply.SimpleString("Background saving started")
            : Reply.Error("ERR Background save already in progress"));
}
