using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>LINDEX key index</c>: the element at an index (negative counts from the end), or null.</summary>
[Command("LINDEX", MinArgs = 2, MaxArgs = 2, Group = "list", Syntax = "key index")]
internal sealed class LIndexHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkString(keyspace.ListIndex(command[0], Arguments.ParseLong(command[1]))));
}
