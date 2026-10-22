using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZREM key member [member ...]</c>: removes members; returns how many were present.</summary>
[Command("ZREM", MinArgs = 2, Flags = CommandFlags.Write, Group = "sorted-set", Syntax = "key member [member ...]")]
internal sealed class ZRemHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.SortedSetRemove(command[0], command.Arguments.Skip(1)) is var removed and > 0
            ? Reply.Integer(removed)
            : Reply.Zero.WithoutPropagation());
}
