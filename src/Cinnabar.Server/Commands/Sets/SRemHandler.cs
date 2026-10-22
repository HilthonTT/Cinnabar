using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SREM key member [member ...]</c>: removes members; returns how many were present.</summary>
[Command("SREM", MinArgs = 2, Flags = CommandFlags.Write, Group = "set", Syntax = "key member [member ...]")]
internal sealed class SRemHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.SetRemove(command[0], command.Arguments.Skip(1)) is var removed and > 0
            ? Reply.Integer(removed)
            : Reply.Zero.WithoutPropagation());
}
