namespace Cinnabar.Storage;

public sealed partial class Keyspace
{
    // ───────────────────────────── Scanning ─────────────────────────────

    /// <summary>
    /// One page of keys (<c>SCAN cursor [MATCH pattern] [COUNT count] [TYPE type]</c>).
    /// Start with cursor 0 and repeat with <see cref="ScanPage{T}.Cursor"/> until it is 0 again.
    /// </summary>
    /// <remarks>
    /// The cursor is a position in a fixed order (a stable 64-bit hash of each key), so a key
    /// that exists for the whole iteration is returned exactly once, however keys are added or
    /// removed between calls; a key added or removed meanwhile may or may not be returned.
    /// <paramref name="count"/> bounds how many keys are examined per call; <paramref name="pattern"/>
    /// and <paramref name="type"/> filter those, so a page can be empty before the end, as in
    /// Redis. Each call walks the whole keyspace to find its page (Redis walks its hash table
    /// incrementally instead).
    /// </remarks>
    public ScanPage<string> Scan(ulong cursor, int count, string? pattern = null, KeyType? type = null)
    {
        long now = NowMs;
        var live = new List<string>();
        foreach ((string key, Entry entry) in _entries)
        {
            if (!entry.IsExpired(now))
            {
                live.Add(key);
            }
        }

        (ulong next, List<string> page) = Page(live, key => key, cursor, count);
        page.RemoveAll(key =>
            (pattern is not null && !KeyPattern.IsMatch(pattern, key)) ||
            (type is { } wanted && GetKeyType(key) != wanted));

        return new ScanPage<string>(next, page);
    }

    /// <summary>One page of a hash's fields and values (<c>HSCAN</c>); see <see cref="Scan"/>.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public ScanPage<KeyValuePair<string, string>> HashScan(string key, ulong cursor, int count, string? pattern = null)
    {
        KeyValuePair<string, string>[] fields;
        lock (StripeFor(key))
        {
            fields = GetLive<Dictionary<string, string>>(key)?.ToArray() ?? [];
        }

        return Filter(Page(fields, pair => pair.Key, cursor, count), pair => pair.Key, pattern);
    }

    /// <summary>One page of a set's members (<c>SSCAN</c>); see <see cref="Scan"/>.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public ScanPage<string> SetScan(string key, ulong cursor, int count, string? pattern = null)
    {
        string[] members;
        lock (StripeFor(key))
        {
            members = GetLive<HashSet<string>>(key)?.ToArray() ?? [];
        }

        return Filter(Page(members, member => member, cursor, count), member => member, pattern);
    }

    /// <summary>One page of a sorted set's members and scores (<c>ZSCAN</c>); see <see cref="Scan"/>.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public ScanPage<ScoredMember> SortedSetScan(string key, ulong cursor, int count, string? pattern = null)
    {
        ScoredMember[] members;
        lock (StripeFor(key))
        {
            members = GetLive<SortedSetValue>(key)?.Ascending.ToArray() ?? [];
        }

        return Filter(Page(members, member => member.Member, cursor, count), member => member.Member, pattern);
    }

    /// <summary>
    /// Orders items by the hash of their name and returns those from <paramref name="cursor"/>
    /// on: at least <paramref name="count"/> of them, plus any that share the last one's hash
    /// (so a hash collision never splits across pages), and the cursor for the next page.
    /// </summary>
    private static (ulong Next, List<T> Page) Page<T>(IEnumerable<T> items, Func<T, string> name, ulong cursor, int count)
    {
        List<(ulong Hash, T Item)> candidates = items
            .Select(item => (Hash: ScanHash(name(item)), Item: item))
            .Where(candidate => candidate.Hash >= cursor)
            .ToList();
        candidates.Sort((a, b) => a.Hash.CompareTo(b.Hash));

        int take = Math.Min(Math.Max(count, 1), candidates.Count);
        while (take < candidates.Count && candidates[take].Hash == candidates[take - 1].Hash)
        {
            take++;
        }

        ulong next = take < candidates.Count ? candidates[take].Hash : 0;
        return (next, candidates.Take(take).Select(candidate => candidate.Item).ToList());
    }

    private static ScanPage<T> Filter<T>((ulong Next, List<T> Page) page, Func<T, string> name, string? pattern)
    {
        if (pattern is not null)
        {
            page.Page.RemoveAll(item => !KeyPattern.IsMatch(pattern, name(item)));
        }

        return new ScanPage<T>(page.Next, page.Page);
    }

    /// <summary>
    /// 64-bit FNV-1a over the string's bytes. It is stable across processes, so cursors stay
    /// valid across restarts and on replicas. A hash of 0 only ever starts an iteration, so
    /// returning it as a "next" cursor (which would mean "done") cannot happen.
    /// </summary>
    private static ulong ScanHash(string value)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in value)
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash;
    }
}
