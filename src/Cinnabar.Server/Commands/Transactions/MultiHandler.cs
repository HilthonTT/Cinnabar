using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Transactions;

/// <summary><c>MULTI</c>: starts queueing commands until EXEC or DISCARD.</summary>
[Command("MULTI", MaxArgs = 0, Flags = CommandFlags.Transaction, Group = "transactions")]
internal sealed class MultiHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (session.InTransaction)
        {
            return ValueTask.FromResult(Reply.Error("ERR MULTI calls can not be nested"));
        }
        
        session.BeginTransaction();
        return ValueTask.FromResult(Reply.Ok);
    }
}
