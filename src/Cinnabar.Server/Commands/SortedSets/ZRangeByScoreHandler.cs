using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>
/// <c>ZRANGEBYSCORE key min max [WITHSCORES] [LIMIT offset count]</c>: members with a score
/// between min and max. <c>(</c> makes a bound exclusive; <c>-inf</c> and <c>+inf</c> are allowed.
/// </summary>
[Command("ZRANGEBYSCORE", MinArgs = 3, Group = "sorted-set", Syntax = "key min max [WITHSCORES] [LIMIT offset count]")]
internal sealed class ZRangeByScoreHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var query = new RangeQuery(command[0], command[1], command[2], RangeBy.Score);
        return ValueTask.FromResult(RangeQuery.Parse(command, 3, query, "WITHSCORES", "LIMIT").Execute(keyspace));
    }
}
