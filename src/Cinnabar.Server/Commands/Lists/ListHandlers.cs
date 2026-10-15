using Cinnabar.Server.Blocking;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>Shared logic for LPUSH and RPUSH.</summary>
internal abstract class PushHandlerBase(Keyspace keyspace, KeyNotifier notifier, ListEnd end) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];
        long length = keyspace.ListPush(key, command.Arguments.Skip(1), end);
        notifier.Notify(key);
        return ValueTask.FromResult(Reply.Integer(length));
    }
}

/// <summary><c>LPUSH key element [element ...]</c>: pushes to the head; returns the new length.</summary>
[Command("LPUSH", MinArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key element [element ...]")]
internal sealed class LPushHandler(Keyspace keyspace, KeyNotifier notifier) : PushHandlerBase(keyspace, notifier, ListEnd.Left);

/// <summary><c>RPUSH key element [element ...]</c>: pushes to the tail; returns the new length.</summary>
[Command("RPUSH", MinArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key element [element ...]")]
internal sealed class RPushHandler(Keyspace keyspace, KeyNotifier notifier) : PushHandlerBase(keyspace, notifier, ListEnd.Right);

/// <summary>Shared logic for LPOP and RPOP.</summary>
internal abstract class PopHandlerBase(Keyspace keyspace, ListEnd end) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];

        if (command.Count == 1)
        {
            IReadOnlyList<string> single = keyspace.ListPop(key, end, 1);
            return ValueTask.FromResult(single.Count > 0
                ? Reply.BulkString(single[0])
                : Reply.NullBulkString.WithoutPropagation());
        }

        int count = Arguments.ParseCount(command[1]);
        if (!keyspace.Exists(key))
        {
            // With a count, a missing key is a null array rather than an empty one.
            // A key of another type falls through and ListPop raises WRONGTYPE.
            return ValueTask.FromResult(Reply.NullArray.WithoutPropagation());
        }

        IReadOnlyList<string> popped = keyspace.ListPop(key, end, count);
        Reply reply = Reply.BulkStringArray(popped);
        return ValueTask.FromResult(popped.Count > 0 ? reply : reply.WithoutPropagation());
    }
}

/// <summary><c>LPOP key [count]</c>: removes from the head. Nil if the key does not exist.</summary>
[Command("LPOP", MinArgs = 1, MaxArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key [count]")]
internal sealed class LPopHandler(Keyspace keyspace) : PopHandlerBase(keyspace, ListEnd.Left);

/// <summary><c>RPOP key [count]</c>: removes from the tail. Nil if the key does not exist.</summary>
[Command("RPOP", MinArgs = 1, MaxArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key [count]")]
internal sealed class RPopHandler(Keyspace keyspace) : PopHandlerBase(keyspace, ListEnd.Right);

/// <summary><c>LLEN key</c>: list length, 0 if the key does not exist.</summary>
[Command("LLEN", MinArgs = 1, MaxArgs = 1, Group = "list", Syntax = "key")]
internal sealed class LLenHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.ListLength(command[0])));
}

/// <summary><c>LRANGE key start stop</c>: inclusive range; negative indexes count from the end.</summary>
[Command("LRANGE", MinArgs = 3, MaxArgs = 3, Group = "list", Syntax = "key start stop")]
internal sealed class LRangeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        long start = Arguments.ParseLong(command[1]);
        long stop = Arguments.ParseLong(command[2]);
        return ValueTask.FromResult(Reply.BulkStringArray(keyspace.ListRange(command[0], start, stop)));
    }
}

/// <summary>
/// Shared logic for BLPOP and BRPOP: pop from the first non-empty list, or wait until one
/// of the lists receives an element or the timeout expires.
/// </summary>
/// <remarks>
/// Inside EXEC the command never blocks, as in Redis. Because blocking writes cannot hold
/// the replication barrier while waiting, the pop is propagated here (as LPOP/RPOP) rather
/// than by the dispatcher.
/// </remarks>
internal abstract class BlockingPopHandlerBase(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
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

/// <summary><c>BLPOP key [key ...] timeout</c>: blocking LPOP. Timeout in seconds; 0 waits forever.</summary>
[Command("BLPOP", MinArgs = 2, Flags = CommandFlags.Write | CommandFlags.Blocking, Group = "list", Syntax = "key [key ...] timeout")]
internal sealed class BLPopHandler(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    ServerSettings settings)
    : BlockingPopHandlerBase(keyspace, notifier, master, settings, ListEnd.Left);

/// <summary><c>BRPOP key [key ...] timeout</c>: blocking RPOP.</summary>
[Command("BRPOP", MinArgs = 2, Flags = CommandFlags.Write | CommandFlags.Blocking, Group = "list", Syntax = "key [key ...] timeout")]
internal sealed class BRPopHandler(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    ServerSettings settings)
    : BlockingPopHandlerBase(keyspace, notifier, master, settings, ListEnd.Right);
