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
