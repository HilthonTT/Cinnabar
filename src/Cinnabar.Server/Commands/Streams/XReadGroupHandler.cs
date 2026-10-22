using Cinnabar.Server.Blocking;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>
/// <c>XREADGROUP GROUP group consumer [COUNT count] [BLOCK milliseconds] [NOACK] STREAMS key [key ...] id [id ...]</c>:
/// reads as a member of a consumer group.
/// </summary>
/// <remarks>
/// <para>
/// The ID <c>&gt;</c> delivers entries no consumer in the group has received yet and records
/// them as pending for this consumer until XACK (unless NOACK). Any other ID re-reads this
/// consumer's own pending entries after it. With BLOCK, and only when every ID is <c>&gt;</c>,
/// waits for new entries; nil on timeout.
/// </para>
/// <para>
/// Like BLPOP, this write cannot hold the transaction gate or the replication barrier while it
/// waits, so each attempt takes them, and deliveries are replicated here as XCLAIM commands
/// (see <see cref="StreamClaims"/>), or XGROUP SETID with NOACK.
/// </para>
/// </remarks>
[Command("XREADGROUP", MinArgs = 6, Flags = CommandFlags.Write | CommandFlags.Blocking, Group = "stream",
    Syntax = "GROUP group consumer [COUNT count] [BLOCK milliseconds] [NOACK] STREAMS key [key ...] id [id ...]")]
internal sealed class XReadGroupHandler(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    TransactionGate gate,
    ServerSettings settings,
    TimeProvider timeProvider) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (!Arguments.Is(command[0], "GROUP"))
        {
            return Reply.Error(Arguments.SyntaxError);
        }

        var request = new Request(command[1], command[2]);
        bool block = false;
        TimeSpan? timeout = null;
        int cursor = 3;

        while (cursor < command.Count && !Arguments.Is(command[cursor], "STREAMS"))
        {
            if (Arguments.Is(command[cursor], "NOACK"))
            {
                request = request with { NoAck = true };
                cursor++;
                continue;
            }

            if (Arguments.Is(command[cursor], "COUNT") && cursor + 1 < command.Count)
            {
                int count = Arguments.ParseCount(command[cursor + 1]);
                request = request with { Count = count == 0 ? null : count };
            }
            else if (Arguments.Is(command[cursor], "BLOCK") && cursor + 1 < command.Count)
            {
                block = true;
                timeout = Arguments.ParseTimeoutMilliseconds(command[cursor + 1]);
            }
            else
            {
                return Reply.Error(Arguments.SyntaxError);
            }

            cursor += 2;
        }

        int remaining = command.Count - cursor - 1;
        if (cursor >= command.Count || remaining <= 0 || remaining % 2 != 0)
        {
            return Reply.Error(
                "ERR Unbalanced 'xreadgroup' list of streams: for each stream key an ID or '>' must be specified.");
        }

        int streamCount = remaining / 2;
        string[] keys = [.. command.Arguments.Skip(cursor + 1).Take(streamCount)];
        var after = new StreamId?[streamCount];
        for (int i = 0; i < streamCount; i++)
        {
            string id = command[cursor + 1 + streamCount + i];
            after[i] = id == ">" ? null : StreamClaims.ParseId(id);
        }

        foreach (string key in keys)
        {
            if (!keyspace.StreamGroupExists(key, request.Group))
            {
                return Reply.Error(
                    $"NOGROUP No such key '{key}' or consumer group '{request.Group}' in XREADGROUP with GROUP option");
            }
        }

        bool canBlock = block && !session.IsExecuting && after.All(id => id is null);
        if (!canBlock)
        {
            return await ReadAsync(request, keys, after, session, cancellationToken) ?? Reply.NullArray.WithoutPropagation();
        }

        using ClientSession.DisconnectWatch watch = session.WatchForDisconnect(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(watch.Token);
        if (timeout is { } limit)
        {
            timeoutCts.CancelAfter(limit);
        }

        while (true)
        {
            using KeyNotifier.KeyWaiter waiter = notifier.Register(keys);
            if (await ReadAsync(request, keys, after, session, cancellationToken) is { } reply)
            {
                return reply;
            }

            try
            {
                await waiter.Signaled.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Reply.NullArray.WithoutPropagation();
            }
        }
    }

    /// <summary>
    /// Reads every stream once and replicates what was delivered. Returns null when nothing
    /// was delivered and every ID was <c>&gt;</c>.
    /// </summary>
    private async ValueTask<Reply?> ReadAsync(
        Request request,
        string[] keys,
        StreamId?[] after,
        ClientSession session,
        CancellationToken cancellationToken)
    {
        bool propagate = !settings.Replication.IsReplica;

        // Inside EXEC the transaction already holds the gate exclusively.
        using SharedExclusiveGate.Releaser? transaction = session.IsExecuting
            ? null
            : await gate.EnterSharedAsync(cancellationToken);

        WriteBarrier.Releaser? barrier = propagate
            ? await master.Barrier.EnterSharedAsync(cancellationToken)
            : null;

        try
        {
            var streams = new List<Reply>();
            for (int i = 0; i < keys.Length; i++)
            {
                long now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                IReadOnlyList<StreamEntry> entries = keyspace.StreamReadGroup(
                    keys[i], request.Group, request.Consumer, after[i], request.Count, request.NoAck);

                if (entries.Count == 0 && after[i] is null)
                {
                    continue;
                }

                streams.Add(Reply.Array([Reply.BulkString(keys[i]), StreamReplies.Entries(entries)]));
                if (propagate && entries.Count > 0)
                {
                    session.LastWriteOffset = master.Propagate(Replication(keys[i], request, after[i], entries, now));
                }
            }

            return streams.Count > 0 ? Reply.Array(streams).WithoutPropagation() : null;
        }
        finally
        {
            barrier?.Dispose();
        }
    }

    private static Command Replication(string key, Request request, StreamId? after, IReadOnlyList<StreamEntry> entries, long now)
    {
        if (after is null && request.NoAck)
        {
            return new Command("XGROUP", "SETID", key, request.Group, entries[^1].Id.ToString());
        }

        // New deliveries start at a delivery count of 1; re-reads add one, as XCLAIM does.
        return after is null
            ? StreamClaims.Replicate(key, request.Group, request.Consumer, entries, now, entries[^1].Id, retryCount: 1)
            : StreamClaims.Replicate(key, request.Group, request.Consumer, entries, now, StreamId.Min);
    }

    private sealed record Request(string Group, string Consumer, int? Count = null, bool NoAck = false);
}
