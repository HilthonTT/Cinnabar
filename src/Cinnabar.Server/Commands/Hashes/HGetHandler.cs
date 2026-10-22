using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HGET key field</c>: the field's value, or nil.</summary>
[Command("HGET", MinArgs = 2, MaxArgs = 2, Group = "hash", Syntax = "key field")]
internal sealed class HGetHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkString(keyspace.HashGet(command[0], command[1])));
}
