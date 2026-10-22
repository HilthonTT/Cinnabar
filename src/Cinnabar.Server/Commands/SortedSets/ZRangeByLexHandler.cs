using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>
/// <c>ZRANGEBYLEX key min max [LIMIT offset count]</c>: members between two lexicographic
/// bounds (<c>[a</c>, <c>(a</c>, <c>-</c>, <c>+</c>); meant for sets whose scores are all equal.
/// </summary>
[Command("ZRANGEBYLEX", MinArgs = 3, Group = "sorted-set", Syntax = "key min max [LIMIT offset count]")]
internal sealed class ZRangeByLexHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var query = new RangeQuery(command[0], command[1], command[2], RangeBy.Lex);
        return ValueTask.FromResult(RangeQuery.Parse(command, 3, query, "LIMIT").Execute(keyspace));
    }
}
