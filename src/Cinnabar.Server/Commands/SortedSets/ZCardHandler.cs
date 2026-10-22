using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZCARD key</c>: the number of members; 0 if the key does not exist.</summary>
[Command("ZCARD", MinArgs = 1, MaxArgs = 1, Group = "sorted-set", Syntax = "key")]
internal sealed class ZCardHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.SortedSetCardinality(command[0])));
}
