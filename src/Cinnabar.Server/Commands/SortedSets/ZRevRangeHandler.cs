using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZREVRANGE key start stop [WITHSCORES]</c>: members by rank, highest score first.</summary>
[Command("ZREVRANGE", MinArgs = 3, MaxArgs = 4, Group = "sorted-set", Syntax = "key start stop [WITHSCORES]")]
internal sealed class ZRevRangeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var query = new RangeQuery(command[0], command[1], command[2], RangeBy.Rank, Reverse: true);
        return ValueTask.FromResult(RangeQuery.Parse(command, 3, query, "WITHSCORES").Execute(keyspace));
    }
}
