using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary><c>STRLEN key</c>: the length of the string in bytes; 0 if the key does not exist.</summary>
[Command("STRLEN", MinArgs = 1, MaxArgs = 1, Group = "string", Syntax = "key")]
internal sealed class StrLenHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.StringLength(command[0])));
}
