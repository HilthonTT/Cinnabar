using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>TYPE key</c>: string, list, stream, hash, set, zset or none.</summary>
[Command("TYPE", MinArgs = 1, MaxArgs = 1, Group = "generic", Syntax = "key")]
internal sealed class TypeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.SimpleString(keyspace.GetKeyType(command[0]).ToRedisName()));
}
