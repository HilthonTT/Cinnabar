using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>
/// <c>SPOP key [count]</c>: removes and returns random members. Without a count, one member
/// or nil; with a count, an array.
/// </summary>
/// <remarks>
/// Replicated as <c>SREM key popped...</c>: replicas must remove the same members, not
/// pick their own at random.
/// </remarks>
[Command("SPOP", MinArgs = 1, MaxArgs = 2, Flags = CommandFlags.Write, Group = "set", Syntax = "key [count]")]
internal sealed class SPopHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];
        int count = command.Count == 2 ? Arguments.ParseCount(command[1]) : 1;

        IReadOnlyList<string> popped = keyspace.SetPop(key, count);
        Reply reply = command.Count == 2
            ? Reply.BulkStringArray(popped)
            : Reply.BulkString(popped.Count > 0 ? popped[0] : null);

        return ValueTask.FromResult(popped.Count > 0
            ? reply.WithPropagation(new Command("SREM", [key, .. popped]))
            : reply.WithoutPropagation());
    }
}
