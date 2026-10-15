using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>TTL key</c>: seconds left (rounded), -1 without TTL, -2 if missing.</summary>
[Command("TTL", MinArgs = 1, MaxArgs = 1, Group = "generic", Syntax = "key")]
internal sealed class TtlHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        long ms = keyspace.GetTimeToLiveMilliseconds(command[0]);
        return ValueTask.FromResult(Reply.Integer(ms < 0 ? ms : (ms + 500) / 1000));
    }
}
