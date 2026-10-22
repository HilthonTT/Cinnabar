using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HEXISTS key field</c>: 1 if the field exists, else 0.</summary>
[Command("HEXISTS", MinArgs = 2, MaxArgs = 2, Group = "hash", Syntax = "key field")]
internal sealed class HExistsHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.HashExists(command[0], command[1]) ? Reply.One : Reply.Zero);
}
