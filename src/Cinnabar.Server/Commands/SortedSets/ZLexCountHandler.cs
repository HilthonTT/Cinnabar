using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZLEXCOUNT key min max</c>: how many members lie between two lexicographic bounds.</summary>
[Command("ZLEXCOUNT", MinArgs = 3, MaxArgs = 3, Group = "sorted-set", Syntax = "key min max")]
internal sealed class ZLexCountHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.SortedSetLexCount(
            command[0], Arguments.ParseLexBound(command[1]), Arguments.ParseLexBound(command[2]))));
}
