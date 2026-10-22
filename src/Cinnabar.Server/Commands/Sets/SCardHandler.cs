using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SCARD key</c>: the number of members; 0 if the key does not exist.</summary>
[Command("SCARD", MinArgs = 1, MaxArgs = 1, Group = "set", Syntax = "key")]
internal sealed class SCardHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.SetCardinality(command[0])));
}
