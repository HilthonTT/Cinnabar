using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>
/// <c>ZRANGE key start stop [BYSCORE | BYLEX] [REV] [LIMIT offset count] [WITHSCORES]</c>:
/// members by rank (default), score or lex order. With REV and BYSCORE/BYLEX, give the
/// upper bound first.
/// </summary>
[Command("ZRANGE", MinArgs = 3, Group = "sorted-set",
    Syntax = "key start stop [BYSCORE | BYLEX] [REV] [LIMIT offset count] [WITHSCORES]")]
internal sealed class ZRangeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var query = new RangeQuery(command[0], command[1], command[2], RangeBy.Rank);
        query = RangeQuery.Parse(command, 3, query, "BYSCORE", "BYLEX", "REV", "LIMIT", "WITHSCORES");
        return ValueTask.FromResult(query.Execute(keyspace));
    }
}
