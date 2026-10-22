using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Transactions;

/// <summary><c>DISCARD</c>: drops the queued commands and ends every WATCH.</summary>
[Command("DISCARD", MaxArgs = 0, Flags = CommandFlags.Transaction, Group = "transactions")]
internal sealed class DiscardHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (!session.InTransaction)
        {
            return ValueTask.FromResult(Reply.Error("ERR DISCARD without MULTI"));
        }

        session.EndTransaction();
        session.UnwatchAll(keyspace);
        return ValueTask.FromResult(Reply.Ok);
    }
}
