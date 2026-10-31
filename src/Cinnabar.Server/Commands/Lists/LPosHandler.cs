using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>
/// <c>LPOS key element [RANK rank] [COUNT num-matches] [MAXLEN len]</c>: the index of a
/// matching element (or null); with COUNT, an array of indexes (COUNT 0 means all).
/// </summary>
[Command("LPOS", MinArgs = 2, Group = "list", Syntax = "key element [RANK rank] [COUNT num-matches] [MAXLEN len]")]
internal sealed class LPosHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        long rank = 1;
        long? count = null;
        long maxLength = 0;

        for (int i = 2; i < command.Count; i += 2)
        {
            if (i + 1 >= command.Count)
            {
                throw new StorageException(Arguments.SyntaxError);
            }

            string option = command[i];
            long value = Arguments.ParseLong(command[i + 1]);
            if (Arguments.Is(option, "RANK"))
            {
                if (value is 0 or long.MinValue)
                {
                    throw new StorageException(
                        "ERR RANK can't be zero: use 1 to start from the first match, 2 from the second ... or use negative to start from the end of the list");
                }

                rank = value;
            }
            else if (Arguments.Is(option, "COUNT"))
            {
                count = value >= 0 ? value : throw new StorageException("ERR COUNT can't be negative");
            }
            else if (Arguments.Is(option, "MAXLEN"))
            {
                maxLength = value >= 0 ? value : throw new StorageException("ERR MAXLEN can't be negative");
            }
            else
            {
                throw new StorageException(Arguments.SyntaxError);
            }
        }

        IReadOnlyList<long> positions = keyspace.ListPositions(command[0], command[1], rank, count ?? 1, maxLength);
        if (count is null)
        {
            return ValueTask.FromResult(positions.Count > 0 ? Reply.Integer(positions[0]) : Reply.NullBulkString);
        }

        return ValueTask.FromResult(Reply.Array(positions.Select(Reply.Integer)));
    }
}
