using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

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
