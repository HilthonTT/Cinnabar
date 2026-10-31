using System.Globalization;
using Cinnabar.Protocol;

namespace Cinnabar.Client;

public sealed partial class RedisClient
{
    /// <summary>
    /// <c>XREAD [COUNT n] [BLOCK ms] STREAMS key [key ...] id [id ...]</c>: entries after the
    /// given ID in each stream (<c>$</c> means "only entries added from now on"). With
    /// <paramref name="block"/>, waits up to that long for data (<see cref="TimeSpan.Zero"/>
    /// waits forever). Returns an empty list on timeout.
    /// </summary>
    /// <param name="streams">Stream key → the ID to read after.</param>
    /// <param name="count">The most entries to return per stream.</param>
    /// <param name="block">How long to wait for data; <see langword="null"/> doesn't wait.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>A blocking read holds a pooled connection while it waits.</remarks>
    public async Task<List<RedisStreamRead>> XReadAsync(
        IReadOnlyCollection<KeyValuePair<string, string>> streams,
        int? count = null,
        TimeSpan? block = null,
        CancellationToken cancellationToken = default)
    {
        List<string> command = ["XREAD"];
        AddReadOptions(command, count, block);
        AddStreams(command, streams);
        return ParseStreamReads(await ExecuteAsync([.. command], cancellationToken).ConfigureAwait(false));
    }

    /// <summary><c>XGROUP CREATE key group id [MKSTREAM]</c>: creates a consumer group that delivers entries after <paramref name="id"/> (<c>$</c>: only new ones).</summary>
    /// <exception cref="RedisException">The group exists (<c>BUSYGROUP</c>), or the stream doesn't and <paramref name="makeStream"/> is not set.</exception>
    public Task XGroupCreateAsync(string key, string group, string id = "$", bool makeStream = false, CancellationToken cancellationToken = default) =>
        ExecuteAsync(makeStream ? ["XGROUP", "CREATE", key, group, id, "MKSTREAM"] : ["XGROUP", "CREATE", key, group, id], cancellationToken);

    /// <summary><c>XGROUP DESTROY key group</c>: <see langword="true"/> if the group existed.</summary>
    public async Task<bool> XGroupDestroyAsync(string key, string group, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["XGROUP", "DESTROY", key, group], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>XGROUP SETID key group id</c>: moves the group's last delivered ID.</summary>
    public Task XGroupSetIdAsync(string key, string group, string id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(["XGROUP", "SETID", key, group, id], cancellationToken);

    /// <summary><c>XGROUP CREATECONSUMER key group consumer</c>: <see langword="true"/> if the consumer was created.</summary>
    public async Task<bool> XGroupCreateConsumerAsync(string key, string group, string consumer, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["XGROUP", "CREATECONSUMER", key, group, consumer], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>XGROUP DELCONSUMER key group consumer</c>: removes a consumer; returns how many pending entries it had.</summary>
    public async Task<long> XGroupDeleteConsumerAsync(string key, string group, string consumer, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["XGROUP", "DELCONSUMER", key, group, consumer], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary>
    /// <c>XREADGROUP GROUP group consumer [COUNT n] [BLOCK ms] [NOACK] STREAMS key [key ...] id [id ...]</c>.
    /// With the ID <c>&gt;</c>, delivers new entries and adds them to the consumer's pending
    /// list; with any other ID, re-reads the consumer's own pending entries after it.
    /// Returns an empty list on timeout.
    /// </summary>
    /// <param name="group">The consumer group.</param>
    /// <param name="consumer">The consumer, created on first use.</param>
    /// <param name="streams">Stream key → <c>&gt;</c> or an ID.</param>
    /// <param name="count">The most entries to return per stream.</param>
    /// <param name="block">How long to wait for new entries; <see langword="null"/> doesn't wait, <see cref="TimeSpan.Zero"/> waits forever.</param>
    /// <param name="noAck">Don't add delivered entries to the pending list (they need no XACK).</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<List<RedisStreamRead>> XReadGroupAsync(
        string group,
        string consumer,
        IReadOnlyCollection<KeyValuePair<string, string>> streams,
        int? count = null,
        TimeSpan? block = null,
        bool noAck = false,
        CancellationToken cancellationToken = default)
    {
        List<string> command = ["XREADGROUP", "GROUP", group, consumer];
        AddReadOptions(command, count, block);
        if (noAck)
        {
            command.Add("NOACK");
        }

        AddStreams(command, streams);
        return ParseStreamReads(await ExecuteAsync([.. command], cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Reads new entries (<c>&gt;</c>) from one stream for a consumer:
    /// <c>XREADGROUP GROUP group consumer [COUNT n] [BLOCK ms] STREAMS key &gt;</c>.
    /// </summary>
    public async Task<List<RedisStreamEntry>> XReadGroupAsync(
        string key,
        string group,
        string consumer,
        int? count = null,
        TimeSpan? block = null,
        CancellationToken cancellationToken = default)
    {
        List<RedisStreamRead> reads = await XReadGroupAsync(
            group, consumer, [new KeyValuePair<string, string>(key, ">")], count, block, noAck: false, cancellationToken).ConfigureAwait(false);
        return reads.Count == 0 ? [] : [.. reads[0].Entries];
    }

    /// <summary><c>XACK key group id [id ...]</c>: acknowledges entries; returns how many were pending.</summary>
    public async Task<long> XAckAsync(string key, string group, params string[] ids)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ids.Length);
        return (await ExecuteAsync(["XACK", key, group, .. ids], CancellationToken.None).ConfigureAwait(false)).AsLong();
    }

    /// <summary><c>XPENDING key group</c>: a summary of the group's pending entries.</summary>
    public async Task<RedisPendingSummary> XPendingAsync(string key, string group, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RespValue> reply = (await ExecuteAsync(["XPENDING", key, group], cancellationToken).ConfigureAwait(false)).AsArray();
        if (reply is not [var count, var smallest, var largest, var consumers])
        {
            throw new RedisException("Unexpected XPENDING reply.");
        }

        var perConsumer = new List<KeyValuePair<string, long>>();
        foreach (RespValue pair in consumers.AsArray())
        {
            if (pair.AsArray() is [var name, var pending])
            {
                perConsumer.Add(new KeyValuePair<string, long>(name.AsString() ?? "", ParseLong(pending)));
            }
        }

        return new RedisPendingSummary(count.AsLong(), smallest.AsString(), largest.AsString(), perConsumer);
    }

    /// <summary>
    /// <c>XPENDING key group [IDLE ms] start end count [consumer]</c>: pending entries between two
    /// IDs (<c>-</c>/<c>+</c> for min/max), optionally only one consumer's or only those idle
    /// for at least <paramref name="minIdle"/>.
    /// </summary>
    public async Task<List<RedisPendingEntry>> XPendingAsync(
        string key,
        string group,
        string start,
        string end,
        int count,
        string? consumer = null,
        TimeSpan? minIdle = null,
        CancellationToken cancellationToken = default)
    {
        List<string> command = ["XPENDING", key, group];
        if (minIdle is { } idle)
        {
            command.AddRange(["IDLE", Format((long)idle.TotalMilliseconds)]);
        }

        command.AddRange([start, end, Format(count)]);
        if (consumer is not null)
        {
            command.Add(consumer);
        }

        var entries = new List<RedisPendingEntry>();
        foreach (RespValue item in (await ExecuteAsync([.. command], cancellationToken).ConfigureAwait(false)).AsArray())
        {
            if (item.AsArray() is [var id, var owner, var idleMs, var deliveries])
            {
                entries.Add(new RedisPendingEntry(
                    id.AsString() ?? "", owner.AsString() ?? "", TimeSpan.FromMilliseconds(idleMs.AsLong()), deliveries.AsLong()));
            }
        }

        return entries;
    }

    /// <summary>
    /// <c>XCLAIM key group consumer min-idle-time id [id ...]</c>: takes ownership of pending
    /// entries idle for at least <paramref name="minIdle"/>; returns the claimed entries.
    /// </summary>
    public async Task<List<RedisStreamEntry>> XClaimAsync(string key, string group, string consumer, TimeSpan minIdle, params string[] ids)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ids.Length);
        RespValue reply = await ExecuteAsync(
            ["XCLAIM", key, group, consumer, Format((long)minIdle.TotalMilliseconds), .. ids], CancellationToken.None).ConfigureAwait(false);
        return ParseEntries(reply);
    }

    /// <summary>
    /// <c>XAUTOCLAIM key group consumer min-idle-time start [COUNT n]</c>: claims pending entries
    /// idle for at least <paramref name="minIdle"/>, scanning the PEL from <paramref name="start"/>.
    /// Call again with <see cref="RedisAutoClaimResult.NextStartId"/> until it is <c>0-0</c>.
    /// </summary>
    public async Task<RedisAutoClaimResult> XAutoClaimAsync(
        string key,
        string group,
        string consumer,
        TimeSpan minIdle,
        string start = "0-0",
        int? count = null,
        CancellationToken cancellationToken = default)
    {
        List<string> command = ["XAUTOCLAIM", key, group, consumer, Format((long)minIdle.TotalMilliseconds), start];
        if (count is { } n)
        {
            command.AddRange(["COUNT", Format(n)]);
        }

        IReadOnlyList<RespValue> reply = (await ExecuteAsync([.. command], cancellationToken).ConfigureAwait(false)).AsArray();
        if (reply.Count < 2)
        {
            throw new RedisException("Unexpected XAUTOCLAIM reply.");
        }

        List<string> deleted = reply.Count > 2 ? [.. reply[2].AsStringList().Select(id => id ?? "")] : [];
        return new RedisAutoClaimResult(reply[0].AsString() ?? "0-0", ParseEntries(reply[1]), deleted);
    }

    private static void AddReadOptions(List<string> command, int? count, TimeSpan? block)
    {
        if (count is { } n)
        {
            command.AddRange(["COUNT", Format(n)]);
        }

        if (block is { } wait)
        {
            command.AddRange(["BLOCK", Format((long)wait.TotalMilliseconds)]);
        }
    }

    private static void AddStreams(List<string> command, IReadOnlyCollection<KeyValuePair<string, string>> streams)
    {
        if (streams.Count == 0)
        {
            throw new ArgumentException("Read at least one stream.", nameof(streams));
        }

        command.Add("STREAMS");
        command.AddRange(streams.Select(s => s.Key));
        command.AddRange(streams.Select(s => s.Value));
    }

    /// <summary>Parses <c>[[key, [[id, [field, value, ...]], ...]], ...]</c>; a null reply (timeout) is empty.</summary>
    private static List<RedisStreamRead> ParseStreamReads(RespValue reply)
    {
        var reads = new List<RedisStreamRead>();
        foreach (RespValue stream in reply.AsArray())
        {
            if (stream.AsArray() is [var key, var entries])
            {
                reads.Add(new RedisStreamRead(key.AsString() ?? "", ParseEntries(entries)));
            }
        }

        return reads;
    }

    /// <summary>Redis sends XPENDING per-consumer counts as bulk strings; accept integers too.</summary>
    private static long ParseLong(RespValue value) =>
        value.Type == RespType.Integer
            ? value.AsLong()
            : long.Parse(value.AsString() ?? "0", NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
}
