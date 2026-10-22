using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZCOUNT key min max</c>: how many members have a score between min and max.</summary>
[Command("ZCOUNT", MinArgs = 3, MaxArgs = 3, Group = "sorted-set", Syntax = "key min max")]
internal sealed class ZCountHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.SortedSetCount(
            command[0], Arguments.ParseScoreBound(command[1]), Arguments.ParseScoreBound(command[2]))));
}
