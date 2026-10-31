using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary>
/// <c>RENAMENX key newkey</c>: like RENAME, but only if <c>newkey</c> does not exist; 1 if
/// renamed, 0 if not.
/// </summary>
[Command("RENAMENX", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key newkey")]
internal sealed class RenameNxHandler(Keyspace keyspace, KeyNotifier notifier) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (!keyspace.Rename(command[0], command[1], onlyIfNotExists: true))
        {
            return ValueTask.FromResult(Reply.Zero.WithoutPropagation());
        }

        notifier.Notify(command[1]);
        return ValueTask.FromResult(Reply.One);
    }
}
