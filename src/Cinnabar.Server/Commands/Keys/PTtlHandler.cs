using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>PTTL key</c>: milliseconds left, -1 without TTL, -2 if missing.</summary>
[Command("PTTL", MinArgs = 1, MaxArgs = 1, Group = "generic", Syntax = "key")]
internal sealed class PTtlHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.GetTimeToLiveMilliseconds(command[0])));
}
