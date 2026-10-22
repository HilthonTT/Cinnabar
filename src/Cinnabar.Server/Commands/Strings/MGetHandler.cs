using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary>
/// <c>MGET key [key ...]</c>: the values of several keys, read atomically. Missing keys and
/// keys of other types are nil.
/// </summary>
[Command("MGET", MinArgs = 1, Group = "string", Syntax = "key [key ...]")]
internal sealed class MGetHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(keyspace.GetStrings(command.Arguments)));
}
