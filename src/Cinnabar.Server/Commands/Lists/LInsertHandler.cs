using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>
/// <c>LINSERT key BEFORE|AFTER pivot element</c>: inserts next to the first element equal to
/// the pivot. Returns the new length, -1 if the pivot is missing, or 0 if the key is missing.
/// </summary>
[Command("LINSERT", MinArgs = 4, MaxArgs = 4, Flags = CommandFlags.Write, Group = "list", Syntax = "key BEFORE|AFTER pivot element")]
internal sealed class LInsertHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        bool before = Arguments.Is(command[1], "BEFORE")
            || (Arguments.Is(command[1], "AFTER") ? false : throw new StorageException(Arguments.SyntaxError));

        long result = keyspace.ListInsert(command[0], before, command[2], command[3]);
        Reply reply = Reply.Integer(result);
        return ValueTask.FromResult(result > 0 ? reply : reply.WithoutPropagation());
    }
}
