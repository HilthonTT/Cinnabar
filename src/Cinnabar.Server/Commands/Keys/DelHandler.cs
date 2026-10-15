using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>DEL key [key ...]</c>: number of keys removed.</summary>
[Command("DEL", MinArgs = 1, Flags = CommandFlags.Write, Group = "generic", Syntax = "key [key ...]")]
internal sealed class DelHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(command.Arguments.Count(keyspace.Delete) is var removed and > 0
            ? Reply.Integer(removed)
            : Reply.Zero.WithoutPropagation());
}
