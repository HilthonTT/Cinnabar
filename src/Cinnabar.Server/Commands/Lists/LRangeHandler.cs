using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>LRANGE key start stop</c>: inclusive range; negative indexes count from the end.</summary>
[Command("LRANGE", MinArgs = 3, MaxArgs = 3, Group = "list", Syntax = "key start stop")]
internal sealed class LRangeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        long start = Arguments.ParseLong(command[1]);
        long stop = Arguments.ParseLong(command[2]);
        return ValueTask.FromResult(Reply.BulkStringArray(keyspace.ListRange(command[0], start, stop)));
    }
}
