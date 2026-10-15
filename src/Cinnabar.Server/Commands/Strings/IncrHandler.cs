using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary><c>INCR key</c>: adds 1 and returns the new value.</summary>
[Command("INCR", MinArgs = 1, MaxArgs = 1, Flags = CommandFlags.Write, Group = "string", Syntax = "key")]
internal sealed class IncrHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.IncrementBy(command[0], 1)));
}
