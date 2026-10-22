namespace Cinnabar.Storage;

/// <summary>
/// The value of a stream key: entries kept sorted by ID, and its consumer groups. Not
/// thread-safe; the <see cref="Keyspace"/> serialises access per key.
/// </summary>
internal sealed class StreamLog
{
    private readonly List<StreamEntry> _entries = [];

    public StreamLog()
    {
    }

    public StreamLog(IEnumerable<StreamEntry> entries)
    {
        foreach (StreamEntry entry in entries)
        {
            if (_entries.Count > 0 && entry.Id.CompareTo(LastId) <= 0)
            {
                throw new ArgumentException("Stream entries must be in strictly increasing ID order.", nameof(entries));
            }

            _entries.Add(entry);
        }
    }

    public int Count => _entries.Count;

    /// <summary>The top ID, or 0-0 for an empty stream.</summary>
    public StreamId LastId => _entries.Count == 0 ? StreamId.Min : _entries[^1].Id;

    public IReadOnlyList<StreamEntry> Entries => _entries;

    /// <summary>Consumer groups by name.</summary>
    public Dictionary<string, StreamConsumerGroup> Groups { get; } = new(StringComparer.Ordinal);

    /// <summary>Finds the entry with exactly <paramref name="id"/>.</summary>
    public bool TryGet(StreamId id, out StreamEntry entry)
    {
        int index = LowerBound(id);
        if (index < _entries.Count && _entries[index].Id == id)
        {
            entry = _entries[index];
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>
    /// Works out the ID XADD should use for <paramref name="requestedId"/>
    /// (<c>*</c>, <c>ms-*</c>, <c>ms-seq</c> or <c>ms</c>).
    /// </summary>
    /// <exception cref="StorageException">The ID is malformed, 0-0, or not greater than the top entry.</exception>
    public StreamId ResolveNewId(string requestedId, long nowMs)
    {
        StreamId last = LastId;

        if (requestedId == "*")
        {
            ulong ms = Math.Max((ulong)Math.Max(nowMs, 0), last.Milliseconds);
            if (ms == last.Milliseconds && _entries.Count > 0)
            {
                return last.Next() ?? throw TooSmall();
            }

            return new StreamId(ms, 0);
        }

        StreamId candidate;
        if (requestedId.EndsWith("-*", StringComparison.Ordinal))
        {
            if (!StreamId.TryParse(requestedId[..^2], 0, out StreamId prefix))
            {
                throw InvalidId();
            }

            ulong seq = prefix.Milliseconds == last.Milliseconds && _entries.Count > 0
                ? last.Sequence + 1
                : prefix.Milliseconds == 0 ? 1UL : 0UL;
            candidate = new StreamId(prefix.Milliseconds, seq);
        }
        else if (!StreamId.TryParse(requestedId, 0, out candidate))
        {
            throw InvalidId();
        }

        if (candidate == StreamId.Min)
        {
            throw new StorageException("ERR The ID specified in XADD must be greater than 0-0");
        }

        if (_entries.Count > 0 && candidate.CompareTo(last) <= 0)
        {
            throw TooSmall();
        }

        return candidate;
    }

    public void Append(StreamEntry entry) => _entries.Add(entry);

    /// <summary>Entries with <paramref name="start"/> &lt;= ID &lt;= <paramref name="end"/>.</summary>
    public List<StreamEntry> Range(StreamId start, StreamId end, int? count)
    {
        var result = new List<StreamEntry>();
        if (start.CompareTo(end) > 0)
        {
            return result;
        }

        for (int i = LowerBound(start); i < _entries.Count; i++)
        {
            StreamEntry entry = _entries[i];
            if (entry.Id.CompareTo(end) > 0 || (count.HasValue && result.Count >= count.Value))
            {
                break;
            }

            result.Add(entry);
        }

        return result;
    }

    /// <summary>Entries with ID strictly greater than <paramref name="after"/>.</summary>
    public List<StreamEntry> ReadAfter(StreamId after, int? count) =>
        after.Next() is { } start ? Range(start, StreamId.Max, count) : [];

    /// <summary>Index of the first entry whose ID is &gt;= <paramref name="id"/>.</summary>
    private int LowerBound(StreamId id)
    {
        int lo = 0;
        int hi = _entries.Count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (_entries[mid].Id.CompareTo(id) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static StorageException InvalidId() =>
        new("ERR Invalid stream ID specified as stream command argument");

    private static StorageException TooSmall() =>
        new("ERR The ID specified in XADD is equal or smaller than the target stream top item");
}
