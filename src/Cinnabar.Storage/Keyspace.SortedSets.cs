namespace Cinnabar.Storage;

public sealed partial class Keyspace
{
    // ───────────────────────────── Sorted sets ─────────────────────────────

    /// <summary>
    /// Adds members or updates their scores (<c>ZADD</c>), creating the sorted set if needed.
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">An increment produced NaN.</exception>
    public SortedSetAddResult SortedSetAdd(
        string key,
        IReadOnlyList<ScoredMember> members,
        SortedSetAddOptions? options = null)
    {
        options ??= SortedSetAddOptions.Default;

        lock (StripeFor(key))
        {
            SortedSetValue? existing = GetLive<SortedSetValue>(key);
            SortedSetValue zset = existing ?? new SortedSetValue();

            long added = 0;
            long changed = 0;
            double? lastScore = null;

            foreach ((string member, double score) in members)
            {
                bool exists = zset.TryGetScore(member, out double current);
                if ((options.Condition == SetCondition.IfNotExists && exists) ||
                    (options.Condition == SetCondition.IfExists && !exists))
                {
                    lastScore = null;
                    continue;
                }

                double target = options.Increment && exists ? current + score : score;
                if (double.IsNaN(target))
                {
                    throw new StorageException("ERR resulting score is not a number (NaN)");
                }

                if (exists &&
                    ((options.Comparison == ScoreComparison.GreaterThan && !(target > current)) ||
                     (options.Comparison == ScoreComparison.LessThan && !(target < current))))
                {
                    lastScore = null;
                    continue;
                }

                if (!exists)
                {
                    added++;
                    changed++;
                }
                else if (!target.Equals(current))
                {
                    changed++;
                }

                zset.Set(member, target);
                lastScore = target;
            }

            if (existing is null)
            {
                if (zset.Count > 0)
                {
                    Store(key, zset, NoExpiry);
                }
            }
            else if (changed > 0)
            {
                Touch(key);
            }

            return new SortedSetAddResult(added, changed, lastScore);
        }
    }

    /// <summary>Removes members (<c>ZREM</c>). A sorted set that becomes empty is deleted.</summary>
    /// <returns>The number of members removed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SortedSetRemove(string key, IEnumerable<string> members)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            if (zset is null)
            {
                return 0;
            }

            long removed = members.Count(zset.Remove);
            RemoveIfEmptyOrTouch(key, zset.Count, removed > 0);
            return removed;
        }
    }

    /// <summary>The scores of several members; missing ones are <see langword="null"/> (<c>ZSCORE</c>, <c>ZMSCORE</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<double?> SortedSetScores(string key, IReadOnlyList<string> members)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            return members
                .Select(member => zset is not null && zset.TryGetScore(member, out double score) ? score : (double?)null)
                .ToArray();
        }
    }

    /// <summary>Number of members, or 0 if the key does not exist (<c>ZCARD</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SortedSetCardinality(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<SortedSetValue>(key)?.Count ?? 0;
        }
    }

    /// <summary>
    /// A member's 0-based rank and its score, or <see langword="null"/> if it is absent
    /// (<c>ZRANK</c>, <c>ZREVRANK</c>).
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public (long Rank, double Score)? SortedSetRank(string key, string member, bool reverse)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            if (zset?.Rank(member, reverse) is not { } rank)
            {
                return null;
            }

            zset.TryGetScore(member, out double score);
            return (rank, score);
        }
    }

    /// <summary>
    /// Members between two inclusive ranks, using <c>LRANGE</c> index rules
    /// (<c>ZRANGE key start stop [REV]</c>).
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<ScoredMember> SortedSetRangeByRank(string key, long start, long stop, bool reverse = false)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            if (zset is null || NormalizeRange(start, stop, zset.Count) is not { } range)
            {
                return [];
            }

            return (reverse ? zset.Descending : zset.Ascending).Skip(range.Skip).Take(range.Take).ToArray();
        }
    }

    /// <summary>
    /// Members whose score is within the bounds (<c>ZRANGE ... BYSCORE</c>). With
    /// <paramref name="reverse"/>, results go from high to low. <paramref name="offset"/> and
    /// <paramref name="count"/> implement <c>LIMIT</c>; a negative count means no limit.
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<ScoredMember> SortedSetRangeByScore(
        string key,
        ScoreBound min,
        ScoreBound max,
        bool reverse = false,
        long offset = 0,
        long count = -1)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            return zset is null ? [] : Limit(zset.ScoreRange(min, max, reverse), offset, count);
        }
    }

    /// <summary>
    /// Members between two lexicographic bounds (<c>ZRANGE ... BYLEX</c>). Meaningful when every
    /// member has the same score, as in Redis.
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<ScoredMember> SortedSetRangeByLex(
        string key,
        LexBound min,
        LexBound max,
        bool reverse = false,
        long offset = 0,
        long count = -1)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            return zset is null ? [] : Limit(zset.LexRange(min, max, reverse), offset, count);
        }
    }

    /// <summary>Number of members with a score within the bounds (<c>ZCOUNT</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SortedSetCount(string key, ScoreBound min, ScoreBound max)
    {
        lock (StripeFor(key))
        {
            return GetLive<SortedSetValue>(key)?.ScoreRange(min, max, reverse: false).LongCount() ?? 0;
        }
    }

    /// <summary>Number of members within lexicographic bounds (<c>ZLEXCOUNT</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SortedSetLexCount(string key, LexBound min, LexBound max)
    {
        lock (StripeFor(key))
        {
            return GetLive<SortedSetValue>(key)?.LexRange(min, max, reverse: false).LongCount() ?? 0;
        }
    }

    /// <summary>
    /// Removes and returns up to <paramref name="count"/> members with the lowest scores, or the
    /// highest with <paramref name="highest"/> (<c>ZPOPMIN</c>, <c>ZPOPMAX</c>).
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<ScoredMember> SortedSetPop(string key, int count, bool highest)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            if (zset is null || count <= 0)
            {
                return [];
            }

            ScoredMember[] popped = [.. (highest ? zset.Descending : zset.Ascending).Take(count)];
            return RemoveMembers(key, zset, popped);
        }
    }

    /// <summary>Removes members between two ranks (<c>ZREMRANGEBYRANK</c>).</summary>
    /// <returns>The number of members removed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SortedSetRemoveRangeByRank(string key, long start, long stop)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            if (zset is null || NormalizeRange(start, stop, zset.Count) is not { } range)
            {
                return 0;
            }

            return RemoveMembers(key, zset, [.. zset.Ascending.Skip(range.Skip).Take(range.Take)]).Length;
        }
    }

    /// <summary>Removes members with a score within the bounds (<c>ZREMRANGEBYSCORE</c>).</summary>
    /// <returns>The number of members removed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SortedSetRemoveRangeByScore(string key, ScoreBound min, ScoreBound max)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            return zset is null ? 0 : RemoveMembers(key, zset, [.. zset.ScoreRange(min, max, reverse: false)]).Length;
        }
    }

    /// <summary>Removes members within lexicographic bounds (<c>ZREMRANGEBYLEX</c>).</summary>
    /// <returns>The number of members removed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SortedSetRemoveRangeByLex(string key, LexBound min, LexBound max)
    {
        lock (StripeFor(key))
        {
            SortedSetValue? zset = GetLive<SortedSetValue>(key);
            return zset is null ? 0 : RemoveMembers(key, zset, [.. zset.LexRange(min, max, reverse: false)]).Length;
        }
    }

    /// <summary>Removes the given members and returns them. Caller holds the key's stripe.</summary>
    private ScoredMember[] RemoveMembers(string key, SortedSetValue zset, ScoredMember[] members)
    {
        foreach (ScoredMember member in members)
        {
            zset.Remove(member.Member);
        }

        RemoveIfEmptyOrTouch(key, zset.Count, members.Length > 0);
        return members;
    }

    private static ScoredMember[] Limit(IEnumerable<ScoredMember> members, long offset, long count)
    {
        if (offset < 0 || count == 0)
        {
            return [];
        }

        IEnumerable<ScoredMember> page = members.Skip((int)Math.Min(offset, int.MaxValue));
        return count < 0 ? [.. page] : [.. page.Take((int)Math.Min(count, int.MaxValue))];
    }

    /// <summary>
    /// Applies <c>LRANGE</c>-style index rules (negative counts from the end, out-of-range
    /// clamped) and returns how many items to skip and take, or <see langword="null"/> for an
    /// empty range.
    /// </summary>
    private static (int Skip, int Take)? NormalizeRange(long start, long stop, long length)
    {
        if (start < 0)
        {
            start = Math.Max(length + start, 0);
        }

        if (stop < 0)
        {
            stop = length + stop;
        }

        stop = Math.Min(stop, length - 1);
        if (start > stop || start >= length)
        {
            return null;
        }

        return ((int)start, (int)(stop - start + 1));
    }
}
