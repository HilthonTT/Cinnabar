using Cinnabar.Server.Blocking;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>
/// Shared logic for BLMOVE and BRPOPLPUSH: move an element like LMOVE, or wait until the
/// source list receives one or the timeout expires (then reply with null).
/// </summary>
/// <remarks>
/// Follows the same rules as <see cref="BlockingPopHandlerBase"/>: never blocks inside EXEC,
/// takes the transaction gate and the replication barrier only for each attempt, and
/// propagates the move itself, as LMOVE.
/// </remarks>
internal abstract class BlockingMoveHandlerBase(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    TransactionGate gate,
    ServerSettings settings) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        (string source, string destination, ListEnd from, ListEnd to, TimeSpan? timeout) = Parse(command);

        using ClientSession.DisconnectWatch watch = session.WatchForDisconnect(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(watch.Token);
        if (timeout is { } limit)
        {
            timeoutCts.CancelAfter(limit);
        }

        while (true)
        {
            using KeyNotifier.KeyWaiter waiter = notifier.Register([source]);

            string? moved = await TryMoveAsync(source, destination, from, to, session, cancellationToken);
            if (moved is not null)
            {
                return Reply.BulkString(moved).WithoutPropagation();
            }

            if (session.IsExecuting)
            {
                return Reply.NullBulkString;
            }

            try
            {
                await waiter.Signaled.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timed out, or the client went away.
                return Reply.NullBulkString;
            }
        }
    }

    /// <summary>Reads the source, destination, directions and timeout from the arguments.</summary>
    protected abstract (string Source, string Destination, ListEnd From, ListEnd To, TimeSpan? Timeout) Parse(Command command);

    private async ValueTask<string?> TryMoveAsync(
        string source,
        string destination,
        ListEnd from,
        ListEnd to,
        ClientSession session,
        CancellationToken cancellationToken)
    {
        bool propagate = !settings.Replication.IsReplica;

        // Inside EXEC the transaction already holds the gate exclusively.
        using SharedExclusiveGate.Releaser? transaction = session.IsExecuting
            ? null
            : await gate.EnterSharedAsync(cancellationToken);

        WriteBarrier.Releaser? releaser = propagate
            ? await master.Barrier.EnterSharedAsync(cancellationToken)
            : null;

        try
        {
            string? value = LMoveHandler.Move(keyspace, notifier, source, destination, from, to);
            if (value is not null && propagate)
            {
                session.LastWriteOffset = master.Propagate(LMoveHandler.AsLMove(source, destination, from, to));
            }

            return value;
        }
        finally
        {
            releaser?.Dispose();
        }
    }
}
