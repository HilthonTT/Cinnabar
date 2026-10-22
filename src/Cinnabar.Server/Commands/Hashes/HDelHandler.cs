using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HDEL key field [field ...]</c>: removes fields; returns how many existed.</summary>
[Command("HDEL", MinArgs = 2, Flags = CommandFlags.Write, Group = "hash", Syntax = "key field [field ...]")]
internal sealed class HDelHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.HashDelete(command[0], command.Arguments.Skip(1)) is var removed and > 0
            ? Reply.Integer(removed)
            : Reply.Zero.WithoutPropagation());
}
