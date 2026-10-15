using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>Formats stream entries as RESP: <c>[[id, [field, value, ...]], ...]</c>.</summary>
internal static class StreamReplies
{
    public static Reply Entries(IEnumerable<StreamEntry> entries) =>
        Reply.Array(entries.Select(entry => Reply.Array(
        [
            Reply.BulkString(entry.Id.ToString()),
            Reply.BulkStringArray(entry.Fields.SelectMany(pair => new[] { pair.Key, pair.Value })),
        ])));
}

/// <summary>
/// <c>XADD key id field value [field value ...]</c>: appends an entry and returns its ID.
/// The ID may be <c>*</c> (auto), <c>ms-*</c> (auto sequence) or explicit <c>ms-seq</c>.
/// </summary>
/// <remarks>Replicated with the generated ID, so replicas store identical IDs.</remarks>
[Command("XADD", MinArgs = 4, Flags = CommandFlags.Write, Group = "stream", Syntax = "key id field value [field value ...]")]
internal sealed class XAddHandler(Keyspace keyspace, KeyNotifier notifier) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if ((command.Count - 2) % 2 != 0)
        {
            return ValueTask.FromResult(Reply.Error("ERR wrong number of arguments for 'xadd' command"));
        }

        string key = command[0];
        var fields = new List<KeyValuePair<string, string>>((command.Count - 2) / 2);
        for (int i = 2; i < command.Count; i += 2)
        {
            fields.Add(new KeyValuePair<string, string>(command[i], command[i + 1]));
        }

        StreamId id = keyspace.StreamAdd(key, command[1], fields);
        notifier.Notify(key);

        string idText = id.ToString();
        var propagated = new Command("XADD", [key, idText, .. command.Arguments.Skip(2)]);
        return ValueTask.FromResult(Reply.BulkString(idText).WithPropagation(propagated));
    }
}

/// <summary><c>XLEN key</c>: number of entries.</summary>
[Command("XLEN", MinArgs = 1, MaxArgs = 1, Group = "stream", Syntax = "key")]
internal sealed class XLenHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.StreamLength(command[0])));
}

/// <summary>
/// <c>XRANGE key start end [COUNT count]</c>: entries between two IDs, inclusive.
/// <c>-</c> and <c>+</c> are the minimum and maximum; prefix an ID with <c>(</c> to exclude it.
/// </summary>
[Command("XRANGE", MinArgs = 3, MaxArgs = 5, Group = "stream", Syntax = "key start end [COUNT count]")]
internal sealed class XRangeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (!StreamId.TryParseRangeBound(command[1], isStart: true, out StreamId? start) ||
            !StreamId.TryParseRangeBound(command[2], isStart: false, out StreamId? end))
        {
            return ValueTask.FromResult(Reply.Error("ERR Invalid stream ID specified as stream command argument"));
        }

        int? count = null;
        if (command.Count > 3)
        {
            if (command.Count != 5 || !Arguments.Is(command[3], "COUNT"))
            {
                return ValueTask.FromResult(Reply.Error(Arguments.SyntaxError));
            }

            count = Arguments.ParseCount(command[4]);
        }

        return ValueTask.FromResult(StreamReplies.Entries(keyspace.StreamRange(command[0], start.Value, end.Value, count)));
    }
}

/// <summary>
/// <c>XREAD [COUNT count] [BLOCK milliseconds] STREAMS key [key ...] id [id ...]</c>:
/// entries newer than each ID. <c>$</c> means "newer than the current last entry".
/// With BLOCK, waits for new entries (0 waits forever); nil on timeout.
/// </summary>
[Command("XREAD", MinArgs = 3, Flags = CommandFlags.Blocking, Group = "stream",
    Syntax = "[COUNT count] [BLOCK milliseconds] STREAMS key [key ...] id [id ...]")]
internal sealed class XReadHandler(Keyspace keyspace, KeyNotifier notifier) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        int? count = null;
        bool block = false;
        TimeSpan? timeout = null;
        int cursor = 0;

        while (cursor < command.Count && !Arguments.Is(command[cursor], "STREAMS"))
        {
            if (Arguments.Is(command[cursor], "COUNT") && cursor + 1 < command.Count)
            {
                count = Arguments.ParseCount(command[cursor + 1]);
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
                "ERR Unbalanced 'xread' list of streams: for each stream key an ID or '$' must be specified.");
        }

        int streamCount = remaining / 2;
        string[] keys = [.. command.Arguments.Skip(cursor + 1).Take(streamCount)];
        var after = new StreamId[streamCount];
        for (int i = 0; i < streamCount; i++)
        {
            string idText = command[cursor + 1 + streamCount + i];
            if (idText == "$")
            {
                // Resolve once, now; later reads compare against this snapshot of the top ID.
                after[i] = keyspace.StreamLastId(keys[i]) ?? StreamId.Min;
            }
            else if (!StreamId.TryParse(idText, 0, out after[i]))
            {
                return Reply.Error("ERR Invalid stream ID specified as stream command argument");
            }
        }

        if (!block || session.IsExecuting)
        {
            return Read(keys, after, count) ?? Reply.NullArray;
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
            if (Read(keys, after, count) is { } reply)
            {
                return reply;
            }

            try
            {
                await waiter.Signaled.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Reply.NullArray;
            }
        }
    }

    /// <summary>Returns <c>[[key, entries], ...]</c> for streams with new entries, or null if there are none.</summary>
    private Reply? Read(string[] keys, StreamId[] after, int? count)
    {
        var streams = new List<Reply>();
        for (int i = 0; i < keys.Length; i++)
        {
            IReadOnlyList<StreamEntry> entries = keyspace.StreamReadAfter(keys[i], after[i], count);
            if (entries.Count > 0)
            {
                streams.Add(Reply.Array([Reply.BulkString(keys[i]), StreamReplies.Entries(entries)]));
            }
        }

        return streams.Count > 0 ? Reply.Array(streams) : null;
    }
}
