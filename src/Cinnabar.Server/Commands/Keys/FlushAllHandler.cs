using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>FLUSHALL [ASYNC | SYNC]</c>: same as FLUSHDB (there is one database).</summary>
[Command("FLUSHALL", MaxArgs = 1, Flags = CommandFlags.Write, Group = "server", Syntax = "[ASYNC | SYNC]")]
internal sealed class FlushAllHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        new FlushDbHandler(keyspace).ExecuteAsync(command, session, cancellationToken);
}
