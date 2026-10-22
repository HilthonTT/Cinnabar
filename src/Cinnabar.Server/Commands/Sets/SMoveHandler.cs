using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>
/// <c>SMOVE source destination member</c>: moves a member between sets atomically; 1 if it
/// was moved, 0 if it was not in the source.
/// </summary>
[Command("SMOVE", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "set", Syntax = "source destination member")]
internal sealed class SMoveHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.SetMove(command[0], command[1], command[2])
            ? Reply.One
            : Reply.Zero.WithoutPropagation());
}
