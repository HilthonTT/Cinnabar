using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>
/// <c>XPENDING key group [[IDLE min-idle-time] start end count [consumer]]</c>: the group's
/// delivered but unacknowledged entries.
/// </summary>
/// <remarks>
/// Without a range: <c>[count, smallest-id, largest-id, [[consumer, count], ...]]</c>. With a
/// range: <c>[[id, consumer, idle-ms, delivery-count], ...]</c>.
/// </remarks>
[Command("XPENDING", MinArgs = 2, MaxArgs = 8, Group = "stream",
    Syntax = "key group [[IDLE min-idle-time] start end count [consumer]]")]
internal sealed class XPendingHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(command.Count == 2 ? Summary(command[0], command[1]) : Extended(command));

    private Reply Summary(string key, string group)
    {
        StreamPendingSummary summary = keyspace.StreamPending(key, group);
        return Reply.Array(
        [
            Reply.Integer(summary.Count),
            Reply.BulkString(summary.Smallest?.ToString()),
            Reply.BulkString(summary.Largest?.ToString()),
            summary.Consumers.Count == 0
                ? Reply.NullArray
                : Reply.Array(summary.Consumers.Select(c => Reply.BulkStringArray([c.Key, Arguments.Format(c.Value)]))),
        ]);
    }

    private Reply Extended(Command command)
    {
        int index = 2;
        long minIdle = 0;
        if (Arguments.Is(command[index], "IDLE"))
        {
            if (command.Count < 4)
            {
                return Reply.Error(Arguments.SyntaxError);
            }

            minIdle = Arguments.ParseLong(command[index + 1]);
            index += 2;
        }

        int rest = command.Count - index;
        if (rest is not (3 or 4))
        {
            return Reply.Error(Arguments.SyntaxError);
        }

        if (!StreamId.TryParseRangeBound(command[index], isStart: true, out StreamId? start) ||
            !StreamId.TryParseRangeBound(command[index + 1], isStart: false, out StreamId? end))
        {
            return Reply.Error("ERR Invalid stream ID specified as stream command argument");
        }

        long count = Math.Max(Arguments.ParseLong(command[index + 2]), 0);
        string? consumer = rest == 4 ? command[index + 3] : null;

        IReadOnlyList<StreamPendingEntry> entries = keyspace.StreamPendingRange(
            command[0], command[1], start.Value, end.Value, (int)Math.Min(count, int.MaxValue), consumer, minIdle);

        return Reply.Array(entries.Select(e => Reply.Array(
        [
            Reply.BulkString(e.Id.ToString()),
            Reply.BulkString(e.Consumer),
            Reply.Integer(e.IdleMilliseconds),
            Reply.Integer(e.DeliveryCount),
        ])));
    }
}
