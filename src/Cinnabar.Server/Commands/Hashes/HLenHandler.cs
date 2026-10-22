using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HLEN key</c>: the number of fields; 0 if the key does not exist.</summary>
[Command("HLEN", MinArgs = 1, MaxArgs = 1, Group = "hash", Syntax = "key")]
internal sealed class HLenHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.HashLength(command[0])));
}
