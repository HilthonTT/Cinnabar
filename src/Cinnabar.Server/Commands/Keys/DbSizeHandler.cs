using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>DBSIZE</c>: number of keys.</summary>
[Command("DBSIZE", MaxArgs = 0, Group = "server")]
internal sealed class DbSizeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.Keys().Count));
}
