using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>Shared logic for ZRANK and ZREVRANK.</summary>
internal abstract class RankHandlerBase(Keyspace keyspace, bool reverse) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        bool withScore = false;
        if (command.Count == 3)
        {
            withScore = Arguments.Is(command[2], "WITHSCORE")
                ? true
                : throw new StorageException(Arguments.SyntaxError);
        }

        (long Rank, double Score)? found = keyspace.SortedSetRank(command[0], command[1], reverse);
        if (found is not { } result)
        {
            return ValueTask.FromResult(withScore ? Reply.NullArray : Reply.NullBulkString);
        }

        return ValueTask.FromResult(withScore
            ? Reply.Array([Reply.Integer(result.Rank), Reply.BulkString(Arguments.Format(result.Score))])
            : Reply.Integer(result.Rank));
    }
}
