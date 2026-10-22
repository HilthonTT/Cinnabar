using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>
/// <c>XAUTOCLAIM key group consumer min-idle-time start [COUNT count] [JUSTID]</c>: scans the
/// pending list from <c>start</c> and claims up to <c>count</c> (default 100) entries idle for
/// at least min-idle-time. Replies <c>[next-start, entries, deleted-ids]</c>; next-start is
/// <c>0-0</c> when the scan is complete.
/// </summary>
/// <remarks>Replicated as an XCLAIM of the exact IDs claimed; see <see cref="StreamClaims"/>.</remarks>
[Command("XAUTOCLAIM", MinArgs = 5, MaxArgs = 8, Flags = CommandFlags.Write, Group = "stream",
    Syntax = "key group consumer min-idle-time start [COUNT count] [JUSTID]")]
internal sealed class XAutoClaimHandler(Keyspace keyspace) : ICommandHandler
{
    private const int DefaultCount = 100;

    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];
        string group = command[1];
        string consumer = command[2];
        long minIdle = Math.Max(Arguments.ParseLong(command[3]), 0);
        if (!StreamId.TryParseRangeBound(command[4], isStart: true, out StreamId? start))
        {
            return ValueTask.FromResult(Reply.Error("ERR Invalid stream ID specified as stream command argument"));
        }

        int count = DefaultCount;
        bool justId = false;
        for (int i = 5; i < command.Count; i++)
        {
            if (Arguments.Is(command[i], "JUSTID"))
            {
                justId = true;
            }
            else if (Arguments.Is(command[i], "COUNT") && i + 1 < command.Count)
            {
                count = Arguments.ParseInt(command[++i]);
                if (count < 1 || count > int.MaxValue / 10)
                {
                    return ValueTask.FromResult(Reply.Error("ERR COUNT must be > 0"));
                }
            }
            else
            {
                return ValueTask.FromResult(Reply.Error(Arguments.SyntaxError));
            }
        }

        StreamClaimResult result = keyspace.StreamAutoClaim(key, group, consumer, minIdle, start.Value, count, justId);
        Reply reply = Reply.Array(
        [
            Reply.BulkString(result.NextCursor.ToString()),
            StreamClaims.Entries(result.Entries, justId),
            Reply.EmptyArray,
        ]);

        return ValueTask.FromResult(result.Entries.Count == 0
            ? reply.WithoutPropagation()
            : reply.WithPropagation(StreamClaims.Replicate(
                key, group, consumer, result.Entries, result.DeliveryTimeMilliseconds, result.LastDeliveredId, justId: justId)));
    }
}
