using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary><c>DECR key</c>: subtracts 1 and returns the new value.</summary>
[Command("DECR", MinArgs = 1, MaxArgs = 1, Flags = CommandFlags.Write, Group = "string", Syntax = "key")]
internal sealed class DecrHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.IncrementBy(command[0], -1)));
}
