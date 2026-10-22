using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HSETNX key field value</c>: sets the field only if it does not exist; 1 if set, else 0.</summary>
[Command("HSETNX", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "hash", Syntax = "key field value")]
internal sealed class HSetNxHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.HashSetIfNotExists(command[0], command[1], command[2])
            ? Reply.One
            : Reply.Zero.WithoutPropagation());
}
