using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary><c>HVALS key</c>: every value.</summary>
[Command("HVALS", MinArgs = 1, MaxArgs = 1, Group = "hash", Syntax = "key")]
internal sealed class HValsHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(keyspace.HashGetAll(command[0]).Select(pair => pair.Value)));
}
