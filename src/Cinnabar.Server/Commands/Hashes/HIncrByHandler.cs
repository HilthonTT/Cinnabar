using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HINCRBY key field increment</c>: adds to an integer field (missing fields start at 0).</summary>
[Command("HINCRBY", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "hash", Syntax = "key field increment")]
internal sealed class HIncrByHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(
            keyspace.HashIncrementBy(command[0], command[1], Arguments.ParseLong(command[2]))));
}
