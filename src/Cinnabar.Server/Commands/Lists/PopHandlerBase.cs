using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>Shared logic for LPOP and RPOP.</summary>
internal abstract class PopHandlerBase(Keyspace keyspace, ListEnd end) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];

        if (command.Count == 1)
        {
            IReadOnlyList<string> single = keyspace.ListPop(key, end, 1);
            return ValueTask.FromResult(single.Count > 0
                ? Reply.BulkString(single[0])
                : Reply.NullBulkString.WithoutPropagation());
        }

        int count = Arguments.ParseCount(command[1]);
        if (!keyspace.Exists(key))
        {
            // With a count, a missing key is a null array rather than an empty one.
            // A key of another type falls through and ListPop raises WRONGTYPE.
            return ValueTask.FromResult(Reply.NullArray.WithoutPropagation());
        }

        IReadOnlyList<string> popped = keyspace.ListPop(key, end, count);
        Reply reply = Reply.BulkStringArray(popped);
        return ValueTask.FromResult(popped.Count > 0 ? reply : reply.WithoutPropagation());
    }
}
