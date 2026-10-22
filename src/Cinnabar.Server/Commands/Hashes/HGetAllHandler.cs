using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HGETALL key</c>: every field and value, flattened as <c>[field, value, ...]</c>.</summary>
[Command("HGETALL", MinArgs = 1, MaxArgs = 1, Group = "hash", Syntax = "key")]
internal sealed class HGetAllHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(
            keyspace.HashGetAll(command[0]).SelectMany(pair => new[] { pair.Key, pair.Value })));
}
