using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZREMRANGEBYSCORE key min max</c>: removes members with a score between min and max; returns how many.</summary>
[Command("ZREMRANGEBYSCORE", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "sorted-set", Syntax = "key min max")]
internal sealed class ZRemRangeByScoreHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ZRemRangeByRankHandler.Removed(keyspace.SortedSetRemoveRangeByScore(
            command[0], Arguments.ParseScoreBound(command[1]), Arguments.ParseScoreBound(command[2]))));
}
