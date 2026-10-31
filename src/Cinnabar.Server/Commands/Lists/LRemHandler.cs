using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>
/// <c>LREM key count element</c>: removes the first <c>count</c> occurrences from the head
/// (positive), from the tail (negative), or all of them (0). Returns how many were removed.
/// </summary>
[Command("LREM", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "list", Syntax = "key count element")]
internal sealed class LRemHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        long removed = keyspace.ListRemove(command[0], Arguments.ParseLong(command[1]), command[2]);
        return ValueTask.FromResult(removed > 0 ? Reply.Integer(removed) : Reply.Zero.WithoutPropagation());
    }
}
