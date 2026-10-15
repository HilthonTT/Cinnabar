using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>PERSIST key</c>: removes the TTL. 1 if one was removed.</summary>
[Command("PERSIST", MinArgs = 1, MaxArgs = 1, Flags = CommandFlags.Write, Group = "generic", Syntax = "key")]
internal sealed class PersistHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.Persist(command[0]) ? Reply.One : Reply.Zero.WithoutPropagation());
}
