namespace Cinnabar.Storage;

public sealed partial class Keyspace
{
    // Rough per-object costs on a 64-bit runtime, used to estimate a key's footprint.
    private const long EntryOverhead = 96;      // Entry object + dictionary slot + key string header
    private const long StringOverhead = 24;     // string object header + length
    private const long ListNodeOverhead = 48;   // LinkedListNode<string>
    private const long HashFieldOverhead = 24;  // Dictionary entry
    private const long SetMemberOverhead = 16;  // HashSet entry
    private const long ZSetMemberOverhead = 72; // dictionary entry + red-black tree node
    private const long StreamEntryOverhead = 64;
    private const long GroupOverhead = 128;         // group object + dictionaries
    private const long ConsumerOverhead = 96;       // consumer object + name + dictionary entry
    private const long PendingEntryOverhead = 96;   // PEL node + pending entry + consumer index
    private const int SizeSamples = 3;

    private readonly SampledKeySet _sampledKeys = new();

    // Keys with a TTL, so volatile-* policies sample only keys they may evict (like Redis's
    // db->expires). Without it, a few TTL keys among many would rarely be sampled at all.
    private readonly SampledKeySet _volatileKeys = new();
    private long _memoryUsage;
    private long _evictedKeys;

    /// <summary>
    /// Estimated bytes used by the stored keys and values. It is an estimate: strings count two
    /// bytes per char plus object overhead, and a collection's size is extrapolated from a few
    /// of its elements, so it tracks growth and shrinkage without walking whole collections.
    /// </summary>
    public long MemoryUsage => Interlocked.Read(ref _memoryUsage);

    /// <summary>Number of keys removed by <see cref="Evict"/> since the keyspace was created.</summary>
    public long EvictedKeys => Interlocked.Read(ref _evictedKeys);

    /// <summary>
    /// Removes keys chosen by <paramref name="policy"/> until <see cref="MemoryUsage"/> is at or
    /// below <paramref name="maxBytes"/>, or no key can be evicted.
    /// </summary>
    /// <param name="maxBytes">The memory limit.</param>
    /// <param name="policy">Which keys may go, and how to choose among them.</param>
    /// <param name="samples">
    /// Keys sampled per eviction (<c>maxmemory-samples</c>). LRU and TTL policies evict the best
    /// candidate among the sample, so more samples approximate true LRU more closely.
    /// </param>
    /// <returns>The evicted keys, so the caller can replicate their deletion.</returns>
    public IReadOnlyList<string> Evict(long maxBytes, EvictionPolicy policy, int samples = 5)
    {
        if (policy == EvictionPolicy.NoEviction || MemoryUsage <= maxBytes)
        {
            return [];
        }

        samples = Math.Max(samples, 1);
        bool volatileOnly = policy is EvictionPolicy.VolatileLru or EvictionPolicy.VolatileRandom or EvictionPolicy.VolatileTtl;
        var evicted = new List<string>();

        // A volatile policy may sample only keys without a TTL; give up after a few empty rounds.
        int fruitlessRounds = 0;
        while (MemoryUsage > maxBytes && fruitlessRounds < 16)
        {
            string? victim = PickVictim(policy, volatileOnly, samples);
            if (victim is null || !TryEvict(victim, volatileOnly))
            {
                fruitlessRounds++;
                continue;
            }

            fruitlessRounds = 0;
            evicted.Add(victim);
        }

        return evicted;
    }

    private string? PickVictim(EvictionPolicy policy, bool volatileOnly, int samples)
    {
        string? best = null;
        long bestScore = long.MaxValue;

        foreach (string key in (volatileOnly ? _volatileKeys : _sampledKeys).Sample(samples))
        {
            // Peek without the stripe: a stale read only makes a slightly worse choice, and
            // TryEvict re-checks under the lock.
            if (!_entries.TryGetValue(key, out Entry? entry) || (volatileOnly && entry.ExpiresAtMs == NoExpiry))
            {
                continue;
            }

            long score = policy switch
            {
                EvictionPolicy.AllKeysLru or EvictionPolicy.VolatileLru => entry.LastAccessMs,
                EvictionPolicy.VolatileTtl => entry.ExpiresAtMs,
                _ => 0, // random: the first eligible key in a random sample
            };

            if (best is null || score < bestScore)
            {
                best = key;
                bestScore = score;
                if (policy is EvictionPolicy.AllKeysRandom or EvictionPolicy.VolatileRandom)
                {
                    break;
                }
            }
        }

        return best;
    }

    private bool TryEvict(string key, bool volatileOnly)
    {
        lock (StripeFor(key))
        {
            if (!_entries.TryGetValue(key, out Entry? entry) || (volatileOnly && entry.ExpiresAtMs == NoExpiry))
            {
                return false;
            }

            if (!Remove(key))
            {
                return false;
            }

            Interlocked.Increment(ref _evictedKeys);
            return true;
        }
    }

    /// <summary>Estimated footprint of one key and its value, in bytes.</summary>
    private static long EstimateSize(string key, object value) =>
        EntryOverhead + (2L * key.Length) + value switch
        {
            string text => StringOverhead + (2L * text.Length),
            LinkedList<string> list => list.Count * (ListNodeOverhead + AverageStringSize(SampleList(list))),
            Dictionary<string, string> hash => hash.Count *
                (HashFieldOverhead + AverageStringSize(hash.Take(SizeSamples).SelectMany(p => (string[])[p.Key, p.Value])) * 2),
            HashSet<string> set => set.Count * (SetMemberOverhead + AverageStringSize(set.Take(SizeSamples))),
            SortedSetValue zset => zset.Count * (ZSetMemberOverhead + AverageStringSize(zset.Ascending.Take(SizeSamples).Select(m => m.Member))),
            StreamLog log => EstimateStreamSize(log),
            _ => 0,
        };

    private static long EstimateStreamSize(StreamLog log)
    {
        // Consumer groups grow independently of the entries: every unacknowledged delivery
        // adds a pending entry, so they must count too.
        long groups = 0;
        foreach ((string name, StreamConsumerGroup group) in log.Groups)
        {
            groups += GroupOverhead + (2L * name.Length) +
                      (group.Consumers.Count * ConsumerOverhead) +
                      (group.Pending.Count * PendingEntryOverhead);
        }

        if (log.Count == 0)
        {
            return StreamEntryOverhead + groups;
        }

        // The newest entry is the best guide to what the stream holds now.
        StreamEntry sample = log.Entries[^1];
        long perEntry = StreamEntryOverhead + sample.Fields.Sum(f => (2 * StringOverhead) + (2L * (f.Key.Length + f.Value.Length)));
        return (log.Count * perEntry) + groups;
    }

    private static IEnumerable<string> SampleList(LinkedList<string> list)
    {
        if (list.First is { } first)
        {
            yield return first.Value;
        }

        if (list.Last is { } last && list.Count > 1)
        {
            yield return last.Value;
        }
    }

    private static long AverageStringSize(IEnumerable<string> sample)
    {
        long total = 0;
        int count = 0;
        foreach (string item in sample)
        {
            total += StringOverhead + (2L * item.Length);
            count++;
        }

        return count == 0 ? StringOverhead : total / count;
    }

    /// <summary>Re-estimates an entry's size after a change and adjusts the total. Caller holds the stripe.</summary>
    private void Resize(string key, Entry entry)
    {
        long size = EstimateSize(key, entry.Value);
        Interlocked.Add(ref _memoryUsage, size - entry.Size);
        entry.Size = size;
    }
}
