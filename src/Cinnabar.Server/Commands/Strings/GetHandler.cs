using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary><c>GET key</c>: the string value, or nil.</summary>
[Command("GET", MinArgs = 1, MaxArgs = 1, Group = "string", Syntax = "key")]
internal sealed class GetHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkString(keyspace.GetString(command[0])));
}
