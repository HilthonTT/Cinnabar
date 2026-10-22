using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>
/// <c>SINTERCARD numkeys key [key ...] [LIMIT limit]</c>: the size of the intersection,
/// stopping once it reaches the limit (0 means no limit).
/// </summary>
[Command("SINTERCARD", MinArgs = 2, Group = "set", Syntax = "numkeys key [key ...] [LIMIT limit]")]
internal sealed class SInterCardHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        int keyCount = Arguments.ParseInt(command[0]);
        if (keyCount <= 0)
        {
            throw new StorageException("ERR numkeys should be greater than 0");
        }

        if (keyCount > command.Count - 1)
        {
            throw new StorageException("ERR Number of keys can't be greater than number of args");
        }

        long limit = 0;
        int rest = 1 + keyCount;
        if (rest < command.Count)
        {
            if (rest + 2 != command.Count || !Arguments.Is(command[rest], "LIMIT"))
            {
                throw new StorageException(Arguments.SyntaxError);
            }

            limit = Arguments.ParseLong(command[rest + 1]);
            if (limit < 0)
            {
                throw new StorageException("ERR LIMIT can't be negative");
            }
        }

        string[] keys = [.. command.Arguments.Skip(1).Take(keyCount)];
        return ValueTask.FromResult(Reply.Integer(keyspace.SetIntersectionCardinality(keys, limit)));
    }
}
