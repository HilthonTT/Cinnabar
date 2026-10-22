using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HSTRLEN key field</c>: the length of the field's value in bytes; 0 if missing.</summary>
[Command("HSTRLEN", MinArgs = 2, MaxArgs = 2, Group = "hash", Syntax = "key field")]
internal sealed class HStrLenHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.HashStringLength(command[0], command[1])));
}
