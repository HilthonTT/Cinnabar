using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>
/// <c>XCLAIM key group consumer min-idle-time id [id ...] [IDLE ms] [TIME unix-ms] [RETRYCOUNT count] [FORCE] [JUSTID] [LASTID id]</c>:
/// takes over pending entries that have been idle for at least min-idle-time.
/// </summary>
/// <remarks>Replicated as an XCLAIM of the exact IDs claimed; see <see cref="StreamClaims"/>.</remarks>
[Command("XCLAIM", MinArgs = 5, Flags = CommandFlags.Write, Group = "stream",
    Syntax = "key group consumer min-idle-time id [id ...] [IDLE ms] [TIME unix-time-milliseconds] [RETRYCOUNT count] [FORCE] [JUSTID] [LASTID id]")]
internal sealed class XClaimHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];
        string group = command[1];
        string consumer = command[2];
        long minIdle = Math.Max(Arguments.ParseLong(command[3]), 0);

        int index = 4;
        var ids = new List<StreamId>();
        while (index < command.Count && StreamId.TryParse(command[index], 0, out StreamId id))
        {
            ids.Add(id);
            index++;
        }

        if (ids.Count == 0)
        {
            return ValueTask.FromResult(Reply.Error("ERR Invalid stream ID specified as stream command argument"));
        }

        var options = new StreamClaimOptions();
        for (; index < command.Count; index++)
        {
            string option = command[index].ToUpperInvariant();
            bool hasValue = index + 1 < command.Count;
            switch (option)
            {
                case "FORCE":
                    options = options with { Force = true };
                    break;
                case "JUSTID":
                    options = options with { JustId = true };
                    break;
                case "IDLE" when hasValue:
                    options = options with { IdleMilliseconds = Math.Max(Arguments.ParseLong(command[++index]), 0) };
                    break;
                case "TIME" when hasValue:
                    options = options with { TimeMilliseconds = Math.Max(Arguments.ParseLong(command[++index]), 0) };
                    break;
                case "RETRYCOUNT" when hasValue:
                    options = options with { RetryCount = Math.Max(Arguments.ParseLong(command[++index]), 0) };
                    break;
                case "LASTID" when hasValue:
                    options = options with { LastId = StreamClaims.ParseId(command[++index]) };
                    break;
                default:
                    return ValueTask.FromResult(Reply.Error($"ERR Unrecognized XCLAIM option '{command[index]}'"));
            }
        }

        StreamClaimResult result = keyspace.StreamClaim(key, group, consumer, minIdle, ids, options);
        Reply reply = StreamClaims.Entries(result.Entries, options.JustId);
        if (result.Entries.Count == 0)
        {
            return ValueTask.FromResult(options.LastId is null
                ? reply.WithoutPropagation()
                : reply.WithPropagation(new Command("XGROUP", "SETID", key, group, result.LastDeliveredId.ToString())));
        }

        return ValueTask.FromResult(reply.WithPropagation(StreamClaims.Replicate(
            key, group, consumer, result.Entries, result.DeliveryTimeMilliseconds, result.LastDeliveredId, options.RetryCount, options.JustId)));
    }
}
