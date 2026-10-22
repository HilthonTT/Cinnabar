using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SMISMEMBER key member [member ...]</c>: 1 or 0 for each member.</summary>
[Command("SMISMEMBER", MinArgs = 2, Group = "set", Syntax = "key member [member ...]")]
internal sealed class SMIsMemberHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Array(
            keyspace.SetContains(command[0], [.. command.Arguments.Skip(1)]).Select(found => found ? Reply.One : Reply.Zero)));
}
