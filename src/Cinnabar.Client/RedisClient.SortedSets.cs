using System.Globalization;
using Cinnabar.Protocol;

namespace Cinnabar.Client;

public sealed partial class RedisClient
{
    // ─────────────────────────── Sorted sets ───────────────────────────

    /// <summary><c>ZADD key score member</c>: <see langword="true"/> if the member is new.</summary>
    public async Task<bool> ZAddAsync(string key, string member, double score, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["ZADD", key, FormatScore(score), member], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>ZADD key score member [score member ...]</c>: the number of members that were new.</summary>
    public async Task<long> ZAddAsync(string key, IEnumerable<RedisSortedSetEntry> entries, CancellationToken cancellationToken = default)
    {
        string[] command = ["ZADD", key, .. entries.SelectMany(e => new[] { FormatScore(e.Score), e.Member })];
        if (command.Length == 2)
        {
            throw new ArgumentException("Add at least one member.", nameof(entries));
        }

        return (await ExecuteAsync(command, cancellationToken).ConfigureAwait(false)).AsLong();
    }

    /// <summary><c>ZINCRBY key increment member</c>: the new score.</summary>
    public async Task<double> ZIncrByAsync(string key, string member, double increment, CancellationToken cancellationToken = default) =>
        ParseScore((await ExecuteAsync(["ZINCRBY", key, FormatScore(increment), member], cancellationToken).ConfigureAwait(false)).AsString())!.Value;

    /// <summary><c>ZSCORE key member</c>: the score, or <see langword="null"/>.</summary>
    public async Task<double?> ZScoreAsync(string key, string member, CancellationToken cancellationToken = default) =>
        ParseScore((await ExecuteAsync(["ZSCORE", key, member], cancellationToken).ConfigureAwait(false)).AsString());

    /// <summary><c>ZRANK key member</c> (or <c>ZREVRANK</c>): the 0-based rank, or <see langword="null"/>.</summary>
    public async Task<long?> ZRankAsync(string key, string member, bool descending = false, CancellationToken cancellationToken = default)
    {
        RespValue reply = await ExecuteAsync([descending ? "ZREVRANK" : "ZRANK", key, member], cancellationToken).ConfigureAwait(false);
        return reply.IsNull ? null : reply.AsLong();
    }

    /// <summary><c>ZCARD key</c>: the number of members.</summary>
    public async Task<long> ZCardAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["ZCARD", key], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>ZCOUNT key min max</c>: how many members have a score in the inclusive range.</summary>
    public async Task<long> ZCountAsync(string key, double min, double max, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["ZCOUNT", key, FormatScore(min), FormatScore(max)], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>ZREM key member [member ...]</c>: the number of members removed.</summary>
    public async Task<long> ZRemAsync(string key, params string[] members) =>
        (await ExecuteAsync(["ZREM", key, .. members], CancellationToken.None).ConfigureAwait(false)).AsLong();

    /// <summary>
    /// <c>ZRANGE key start stop [REV] WITHSCORES</c>: members between two ranks (inclusive;
    /// negative counts from the end), lowest score first unless <paramref name="descending"/>.
    /// </summary>
    public async Task<List<RedisSortedSetEntry>> ZRangeAsync(
        string key,
        long start = 0,
        long stop = -1,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        string[] command = descending
            ? ["ZRANGE", key, Format(start), Format(stop), "REV", "WITHSCORES"]
            : ["ZRANGE", key, Format(start), Format(stop), "WITHSCORES"];
        return ParseEntriesWithScores(await ExecuteAsync(command, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// <c>ZRANGE key min max BYSCORE [REV] [LIMIT offset count] WITHSCORES</c>: members with a
    /// score in the inclusive range, lowest first unless <paramref name="descending"/>.
    /// </summary>
    public async Task<List<RedisSortedSetEntry>> ZRangeByScoreAsync(
        string key,
        double min = double.NegativeInfinity,
        double max = double.PositiveInfinity,
        bool descending = false,
        long offset = 0,
        long? count = null,
        CancellationToken cancellationToken = default)
    {
        List<string> command = descending
            ? ["ZRANGE", key, FormatScore(max), FormatScore(min), "BYSCORE", "REV"]
            : ["ZRANGE", key, FormatScore(min), FormatScore(max), "BYSCORE"];
        if (offset != 0 || count is not null)
        {
            command.AddRange(["LIMIT", Format(offset), Format(count ?? -1)]);
        }

        command.Add("WITHSCORES");
        return ParseEntriesWithScores(await ExecuteAsync([.. command], cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// <c>ZPOPMIN key count</c> (or <c>ZPOPMAX</c>): removes and returns up to
    /// <paramref name="count"/> members with the lowest (or highest) scores.
    /// </summary>
    public async Task<List<RedisSortedSetEntry>> ZPopAsync(
        string key,
        int count = 1,
        bool highest = false,
        CancellationToken cancellationToken = default) =>
        ParseEntriesWithScores(await ExecuteAsync([highest ? "ZPOPMAX" : "ZPOPMIN", key, Format(count)], cancellationToken).ConfigureAwait(false));

    private static List<RedisSortedSetEntry> ParseEntriesWithScores(RespValue reply)
    {
        IReadOnlyList<RespValue> flat = reply.AsArray();
        var entries = new List<RedisSortedSetEntry>(flat.Count / 2);
        for (int i = 0; i + 1 < flat.Count; i += 2)
        {
            entries.Add(new RedisSortedSetEntry(flat[i].AsString() ?? "", ParseScore(flat[i + 1].AsString())!.Value));
        }

        return entries;
    }

    /// <summary>Formats a score as Redis expects: shortest round-trip text, or <c>inf</c>/<c>-inf</c>.</summary>
    private static string FormatScore(double score) => score switch
    {
        double.PositiveInfinity => "+inf",
        double.NegativeInfinity => "-inf",
        _ => score.ToString("R", CultureInfo.InvariantCulture),
    };

    /// <summary>Parses a score reply, which may be <c>inf</c> or <c>-inf</c>.</summary>
    private static double? ParseScore(string? text) => text?.ToLowerInvariant() switch
    {
        null => null,
        "inf" or "+inf" => double.PositiveInfinity,
        "-inf" => double.NegativeInfinity,
        _ => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
    };
}
