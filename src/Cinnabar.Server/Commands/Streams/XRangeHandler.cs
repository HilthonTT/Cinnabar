using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

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
