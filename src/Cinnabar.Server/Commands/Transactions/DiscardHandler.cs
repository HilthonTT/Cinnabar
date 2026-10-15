using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Transactions;

/// <summary><c>DISCARD</c>: drops the queued commands.</summary>
[Command("DISCARD", MaxArgs = 0, Flags = CommandFlags.Transaction, Group = "transactions")]
internal sealed class DiscardHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (!session.InTransaction)
        {
            return ValueTask.FromResult(Reply.Error("ERR DISCARD without MULTI"));
        }

        session.EndTransaction();
        return ValueTask.FromResult(Reply.Ok);
    }
}
