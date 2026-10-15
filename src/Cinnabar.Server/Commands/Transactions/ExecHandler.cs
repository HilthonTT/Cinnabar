using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Transactions;

/// <summary>
/// <c>EXEC</c>: runs the queued commands and returns their replies as an array.
/// Aborts with EXECABORT if a command was rejected while queueing.
/// </summary>
[Command("EXEC", MaxArgs = 0, Flags = CommandFlags.Transaction, Group = "transactions")]
internal sealed class ExecHandler(Lazy<CommandDispatcher> dispatcher) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        session.InTransaction
            ? dispatcher.Value.ExecuteTransactionAsync(session, cancellationToken)
            : ValueTask.FromResult(Reply.Error("ERR EXEC without MULTI"));
}
