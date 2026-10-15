using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>LLEN key</c>: list length, 0 if the key does not exist.</summary>
[Command("LLEN", MinArgs = 1, MaxArgs = 1, Group = "list", Syntax = "key")]
internal sealed class LLenHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.ListLength(command[0])));
}
