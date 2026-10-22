using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HMGET key field [field ...]</c>: the values of several fields; nil for missing ones.</summary>
[Command("HMGET", MinArgs = 2, Group = "hash", Syntax = "key field [field ...]")]
internal sealed class HMGetHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(keyspace.HashGetMany(command[0], [.. command.Arguments.Skip(1)])));
}
