using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SISMEMBER key member</c>: 1 if the member is in the set, else 0.</summary>
[Command("SISMEMBER", MinArgs = 2, MaxArgs = 2, Group = "set", Syntax = "key member")]
internal sealed class SIsMemberHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.SetContains(command[0], [command[1]])[0] ? Reply.One : Reply.Zero);
}
