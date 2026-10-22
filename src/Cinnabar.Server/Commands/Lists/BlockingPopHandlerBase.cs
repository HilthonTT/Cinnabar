using Cinnabar.Server.Blocking;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>
/// Shared logic for BLPOP and BRPOP: pop from the first non-empty list, or wait until one
/// of the lists receives an element or the timeout expires.
/// </summary>
/// <remarks>
/// Inside EXEC the command never blocks, as in Redis. Because blocking writes cannot hold
/// the transaction gate or the replication barrier while waiting, each pop attempt takes
/// them itself, and the pop is propagated here (as LPOP/RPOP) rather than by the dispatcher.
/// </remarks>
internal abstract class BlockingPopHandlerBase(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    TransactionGate gate,
    ServerSettings settings,
    ListEnd end) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string[] keys = [.. command.Arguments.Take(command.Count - 1)];
        TimeSpan? timeout = Arguments.ParseTimeoutSeconds(command[^1]);

        using ClientSession.DisconnectWatch watch = session.WatchForDisconnect(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(watch.Token);
        if (timeout is { } limit)
        {
            timeoutCts.CancelAfter(limit);
        }

        while (true)
        {
            using KeyNotifier.KeyWaiter waiter = notifier.Register(keys);

            Reply? popped = await TryPopAsync(keys, session, cancellationToken);
            if (popped is not null)
            {
                return popped;
            }

            if (session.IsExecuting)
            {
                return Reply.NullArray;
            }

            try
            {
                await waiter.Signaled.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timed out, or the client went away.
                return Reply.NullArray;
            }
        }
    }

    private async ValueTask<Reply?> TryPopAsync(string[] keys, ClientSession session, CancellationToken cancellationToken)
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
            foreach (string key in keys)
            {
                IReadOnlyList<string> values = keyspace.ListPop(key, end, 1);
                if (values.Count == 0)
                {
                    continue;
                }

                if (propagate)
                {
                    string popCommand = end == ListEnd.Left ? "LPOP" : "RPOP";
                    session.LastWriteOffset = master.Propagate(new Command(popCommand, key));
                }

                return Reply.BulkStringArray([key, values[0]]).WithoutPropagation();
            }

            return null;
        }
        finally
        {
            releaser?.Dispose();
        }
    }
}
