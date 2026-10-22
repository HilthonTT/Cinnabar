using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>
/// Builds the <c>XCLAIM</c> commands that replicate consumer-group deliveries.
/// </summary>
/// <remarks>
/// XREADGROUP, XCLAIM and XAUTOCLAIM depend on the master's clock (idle times) and, for
/// XREADGROUP, on blocking. Like Redis, the master replicates what they did instead: an
/// XCLAIM with min-idle 0 for the exact IDs, the delivery time, and the group's last delivered
/// ID, which replays deterministically on a replica that has the same state.
/// </remarks>
internal static class StreamClaims
{
    /// <summary>
    /// <c>XCLAIM key group consumer 0 id... TIME ms [RETRYCOUNT n] [JUSTID] FORCE LASTID id</c>.
    /// </summary>
    public static Command Replicate(
        string key,
        string group,
        string consumer,
        IEnumerable<StreamEntry> entries,
        long deliveryTimeMs,
        StreamId lastDeliveredId,
        long? retryCount = null,
        bool justId = false)
    {
        List<string> parts = [key, group, consumer, "0", .. entries.Select(e => e.Id.ToString())];
        parts.AddRange(["TIME", Arguments.Format(deliveryTimeMs)]);
        if (retryCount is { } retries)
        {
            parts.AddRange(["RETRYCOUNT", Arguments.Format(retries)]);
        }

        if (justId)
        {
            parts.Add("JUSTID");
        }

        parts.AddRange(["FORCE", "LASTID", lastDeliveredId.ToString()]);
        return new Command("XCLAIM", [.. parts]);
    }

    /// <summary>Formats claimed entries, or just their IDs.</summary>
    public static Reply Entries(IReadOnlyList<StreamEntry> entries, bool justId) =>
        justId ? Reply.BulkStringArray(entries.Select(e => e.Id.ToString())) : StreamReplies.Entries(entries);

    /// <summary>Parses an entry ID, or throws the Redis error for an invalid one.</summary>
    public static StreamId ParseId(string text) =>
        StreamId.TryParse(text, 0, out StreamId id)
            ? id
            : throw new StorageException("ERR Invalid stream ID specified as stream command argument");
}
