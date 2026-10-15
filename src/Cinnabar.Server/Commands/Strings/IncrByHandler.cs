using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary><c>INCRBY key increment</c></summary>
[Command("INCRBY", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "string", Syntax = "key increment")]
internal sealed class IncrByHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.IncrementBy(command[0], Arguments.ParseLong(command[1]))));
}
