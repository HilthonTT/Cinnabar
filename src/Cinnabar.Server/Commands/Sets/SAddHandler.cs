using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SADD key member [member ...]</c>: adds members; returns how many were new.</summary>
[Command("SADD", MinArgs = 2, Flags = CommandFlags.Write, Group = "set", Syntax = "key member [member ...]")]
internal sealed class SAddHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.SetAdd(command[0], command.Arguments.Skip(1)) is var added and > 0
            ? Reply.Integer(added)
            : Reply.Zero.WithoutPropagation());
}
