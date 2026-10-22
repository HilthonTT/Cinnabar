using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>
/// <c>ZREVRANGEBYSCORE key max min [WITHSCORES] [LIMIT offset count]</c>: like ZRANGEBYSCORE,
/// highest score first.
/// </summary>
[Command("ZREVRANGEBYSCORE", MinArgs = 3, Group = "sorted-set", Syntax = "key max min [WITHSCORES] [LIMIT offset count]")]
internal sealed class ZRevRangeByScoreHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var query = new RangeQuery(command[0], command[1], command[2], RangeBy.Score, Reverse: true);
        return ValueTask.FromResult(RangeQuery.Parse(command, 3, query, "WITHSCORES", "LIMIT").Execute(keyspace));
    }
}
