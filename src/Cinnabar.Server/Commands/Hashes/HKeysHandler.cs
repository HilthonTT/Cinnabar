using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HKEYS key</c>: every field name.</summary>
[Command("HKEYS", MinArgs = 1, MaxArgs = 1, Group = "hash", Syntax = "key")]
internal sealed class HKeysHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(keyspace.HashGetAll(command[0]).Select(pair => pair.Key)));
}
