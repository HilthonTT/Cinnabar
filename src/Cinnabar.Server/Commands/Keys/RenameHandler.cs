using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary>
/// <c>RENAME key newkey</c>: renames a key, keeping its value and TTL and overwriting
/// <c>newkey</c>. An error if <c>key</c> does not exist.
/// </summary>
[Command("RENAME", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key newkey")]
internal sealed class RenameHandler(Keyspace keyspace, KeyNotifier notifier) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        keyspace.Rename(command[0], command[1]);

        // A list or stream may have appeared under the new name: wake anyone blocked on it.
        notifier.Notify(command[1]);
        return ValueTask.FromResult(Reply.Ok);
    }
}
