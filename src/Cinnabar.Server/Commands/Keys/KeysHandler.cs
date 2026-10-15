using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>KEYS pattern</c>: keys matching a glob (<c>*</c>, <c>?</c>, <c>[a-z]</c>).</summary>
[Command("KEYS", MinArgs = 1, MaxArgs = 1, Group = "generic", Syntax = "pattern")]
internal sealed class KeysHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(keyspace.Keys(command[0])));
}
