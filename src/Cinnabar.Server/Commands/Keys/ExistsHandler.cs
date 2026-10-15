using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>EXISTS key [key ...]</c>: how many of the keys exist (repeats count again).</summary>
[Command("EXISTS", MinArgs = 1, Group = "generic", Syntax = "key [key ...]")]
internal sealed class ExistsHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(command.Arguments.Count(keyspace.Exists)));
}
