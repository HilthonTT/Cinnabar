using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Transactions;

/// <summary><c>UNWATCH</c>: forgets every key watched by this connection.</summary>
[Command("UNWATCH", MaxArgs = 0, Group = "transactions")]
internal sealed class UnwatchHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        session.UnwatchAll(keyspace);
        return ValueTask.FromResult(Reply.Ok);
    }
}
