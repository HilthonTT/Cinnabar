using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZREVRANGEBYLEX key max min [LIMIT offset count]</c>: like ZRANGEBYLEX, in reverse.</summary>
[Command("ZREVRANGEBYLEX", MinArgs = 3, Group = "sorted-set", Syntax = "key max min [LIMIT offset count]")]
internal sealed class ZRevRangeByLexHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var query = new RangeQuery(command[0], command[1], command[2], RangeBy.Lex, Reverse: true);
        return ValueTask.FromResult(RangeQuery.Parse(command, 3, query, "LIMIT").Execute(keyspace));
    }
}
