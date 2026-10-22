using System.Collections.Concurrent;
using System.Globalization;

namespace Cinnabar.Storage;

/// <summary>
/// The database: one map from key to value, where a value is a string, list, stream, hash,
/// set or sorted set.
/// </summary>
/// <remarks>
/// <para>
/// Every key lives in exactly one place, so a key can never hold two types at once, and
/// expiry works the same way for every type.
/// </para>
/// <para>
/// Thread safety: each key hashes to one of a fixed set of lock stripes, and every operation
/// on a key runs while holding that key's stripe. Operations on a single key are therefore
/// atomic; operations on different keys run in parallel. Multi-key operations (<c>MSET</c>,
/// <c>SMOVE</c>, <c>SINTERSTORE</c>, ...) take all their stripes at once, always in ascending
/// stripe order, so they are atomic too and cannot deadlock with each other.
/// </para>
/// <para>
/// Every change to a key gives it a new version (see <see cref="Touch(Entry)"/>).
/// </para>
/// <para>
/// Expired keys are removed lazily on access and by a periodic sweep.
/// </para>
/// </remarks>
public sealed partial class Keyspace : IDisposable
{
    private const int StripeCount = 256;
    private const long NoExpiry = 0;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private long _version;
    private readonly object[] _stripes;
    private readonly TimeProvider _time;
    private readonly ITimer? _evictionTimer;

    /// <summary>Creates an empty keyspace.</summary>
    /// <param name="timeProvider">Clock used for expiry; defaults to the system clock.</param>
    /// <param name="evictionInterval">
    /// How often to sweep expired keys. <see cref="TimeSpan.Zero"/> disables the sweep
    /// (expired keys are still invisible and are removed on access). Defaults to one second.
    /// </param>
    public Keyspace(TimeProvider? timeProvider = null, TimeSpan? evictionInterval = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _stripes = new object[StripeCount];
        for (int i = 0; i < _stripes.Length; i++)
        {
            _stripes[i] = new object();
        }

        TimeSpan interval = evictionInterval ?? TimeSpan.FromSeconds(1);
        if (interval > TimeSpan.Zero)
        {
            _evictionTimer = _time.CreateTimer(_ => EvictExpired(), null, interval, interval);
        }
    }

    /// <summary>Number of keys, possibly including expired keys not yet swept.</summary>
    public int Count => _entries.Count;

    private long NowMs => _time.GetUtcNow().ToUnixTimeMilliseconds();


    /// <summary>Returns the live keys matching a <see cref="KeyPattern"/> glob, in ordinal order.</summary>
    public IReadOnlyList<string> Keys(string pattern = "*")
    {
        long now = NowMs;
        var keys = new List<string>();
        foreach ((string key, Entry entry) in _entries)
        {
            if (!entry.IsExpired(now) && (pattern == "*" || KeyPattern.IsMatch(pattern, key)))
            {
                keys.Add(key);
            }
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>Returns the type stored at <paramref name="key"/>, or <see cref="KeyType.None"/>.</summary>
    public KeyType GetKeyType(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive(key)?.Value switch
            {
                string => KeyType.String,
                LinkedList<string> => KeyType.List,
                StreamLog => KeyType.Stream,
                Dictionary<string, string> => KeyType.Hash,
                HashSet<string> => KeyType.Set,
                SortedSetValue => KeyType.SortedSet,
                _ => KeyType.None,
            };
        }
    }

    /// <summary>Returns <see langword="true"/> if the key exists and has not expired.</summary>
    public bool Exists(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive(key) is not null;
        }
    }

    /// <summary>Removes a key of any type. Returns <see langword="true"/> if it existed.</summary>
    public bool Delete(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive(key) is not null && Remove(key);
        }
    }

    /// <summary>Removes every key.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// Sets a time to live. A zero or negative TTL deletes the key, as in Redis.
    /// Returns <see langword="false"/> if the key does not exist.
    /// </summary>
    public bool Expire(string key, TimeSpan ttl) =>
        ExpireAt(key, SaturatingAdd(NowMs, (long)ttl.TotalMilliseconds));

    /// <summary>
    /// Sets an absolute expiry as Unix time in milliseconds. A time that is not in the future
    /// deletes the key. Returns <see langword="false"/> if the key does not exist.
    /// </summary>
    public bool ExpireAt(string key, long unixTimeMilliseconds)
    {
        lock (StripeFor(key))
        {
            Entry? entry = GetLive(key);
            if (entry is null)
            {
                return false;
            }

            if (unixTimeMilliseconds <= NowMs)
            {
                Remove(key);
                return true;
            }

            entry.ExpiresAtMs = unixTimeMilliseconds;
            Touch(entry);
            return true;
        }
    }

    /// <summary>Removes the TTL. Returns <see langword="true"/> if the key had one.</summary>
    public bool Persist(string key)
    {
        lock (StripeFor(key))
        {
            Entry? entry = GetLive(key);
            if (entry is null || entry.ExpiresAtMs == NoExpiry)
            {
                return false;
            }

            entry.ExpiresAtMs = NoExpiry;
            Touch(entry);
            return true;
        }
    }

    /// <summary>
    /// Remaining time to live in milliseconds: <c>-2</c> if the key does not exist,
    /// <c>-1</c> if it has no TTL.
    /// </summary>
    public long GetTimeToLiveMilliseconds(string key)
    {
        lock (StripeFor(key))
        {
            Entry? entry = GetLive(key);
            if (entry is null)
            {
                return -2;
            }

            return entry.ExpiresAtMs == NoExpiry ? -1 : Math.Max(entry.ExpiresAtMs - NowMs, 0);
        }
    }

    // ───────────────────────────── Strings ─────────────────────────────

    /// <summary>Returns the string at <paramref name="key"/>, or <see langword="null"/> if missing.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public string? GetString(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<string>(key);
        }
    }

    /// <summary>
    /// Stores a string, replacing a value of any type (like Redis <c>SET</c>).
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="expiry">Optional TTL. Ignored when <paramref name="keepTtl"/> is set.</param>
    /// <param name="keepTtl">Keep the key's existing TTL (<c>KEEPTTL</c>).</param>
    /// <param name="condition">Only write if the key does / does not exist (<c>XX</c> / <c>NX</c>).</param>
    /// <returns><see langword="false"/> if <paramref name="condition"/> prevented the write.</returns>
    public bool SetString(
        string key,
        string value,
        TimeSpan? expiry = null,
        bool keepTtl = false,
        SetCondition condition = SetCondition.Always) =>
        SetStringCore(
            key,
            value,
            expiry is { } ttl ? SaturatingAdd(NowMs, Math.Max((long)ttl.TotalMilliseconds, 1)) : null,
            keepTtl,
            condition);

    /// <summary>
    /// Like <see cref="SetString"/>, with an absolute expiry in Unix milliseconds
    /// (<see langword="null"/> for none). Avoids <see cref="TimeSpan"/> range limits for very
    /// distant expiry times.
    /// </summary>
    public bool SetStringExpiringAt(
        string key,
        string value,
        long? expiresAtUnixMilliseconds,
        bool keepTtl = false,
        SetCondition condition = SetCondition.Always) =>
        SetStringCore(key, value, expiresAtUnixMilliseconds, keepTtl, condition);

    private bool SetStringCore(string key, string value, long? expiresAtMs, bool keepTtl, SetCondition condition)
    {
        lock (StripeFor(key))
        {
            Entry? existing = GetLive(key);
            if ((condition == SetCondition.IfNotExists && existing is not null) ||
                (condition == SetCondition.IfExists && existing is null))
            {
                return false;
            }

            long expiresAt = keepTtl ? existing?.ExpiresAtMs ?? NoExpiry : expiresAtMs ?? NoExpiry;

            Store(key, value, expiresAt);
            return true;
        }
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to the integer stored as a string at <paramref name="key"/>,
    /// creating it from 0 if missing. The key's TTL is preserved.
    /// </summary>
    /// <returns>The new value.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">The value is not an integer, or the result would overflow.</exception>
    public long IncrementBy(string key, long delta)
    {
        lock (StripeFor(key))
        {
            Entry? entry = GetLive(key);
            long current = 0;

            if (entry is not null)
            {
                if (entry.Value is not string text)
                {
                    throw new WrongTypeException();
                }

                if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out current))
                {
                    throw new StorageException("ERR value is not an integer or out of range");
                }
            }

            long result;
            try
            {
                result = checked(current + delta);
            }
            catch (OverflowException)
            {
                throw new StorageException("ERR increment or decrement would overflow");
            }

            string newValue = result.ToString(CultureInfo.InvariantCulture);
            if (entry is null)
            {
                Store(key, newValue, NoExpiry);
            }
            else
            {
                entry.Value = newValue;
                Touch(entry);
            }

            return result;
        }
    }

    // ───────────────────────────── Lists ─────────────────────────────

    /// <summary>
    /// Pushes values one at a time onto <paramref name="end"/>, creating the list if needed.
    /// <c>LPUSH k a b c</c> therefore yields <c>[c, b, a]</c>.
    /// </summary>
    /// <returns>The list length after the push.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long ListPush(string key, IEnumerable<string> values, ListEnd end)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? existing = GetLive<LinkedList<string>>(key);
            LinkedList<string> list = existing ?? new LinkedList<string>();
            int before = list.Count;

            foreach (string value in values)
            {
                if (end == ListEnd.Left)
                {
                    list.AddFirst(value);
                }
                else
                {
                    list.AddLast(value);
                }
            }

            // Redis never stores an empty list: pushing nothing onto a missing key creates nothing.
            if (existing is null && list.Count > 0)
            {
                Store(key, list, NoExpiry);
            }
            else if (list.Count != before)
            {
                Touch(key);
            }

            return list.Count;
        }
    }

    /// <summary>
    /// Removes up to <paramref name="count"/> elements from <paramref name="end"/>.
    /// A list that becomes empty is deleted, as in Redis.
    /// </summary>
    /// <returns>The removed elements, in removal order; empty if the key does not exist.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<string> ListPop(string key, ListEnd end, int count = 1)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? list = GetLive<LinkedList<string>>(key);
            if (list is null || count <= 0)
            {
                return [];
            }

            var popped = new List<string>(Math.Min(count, list.Count));
            while (popped.Count < count && list.Count > 0)
            {
                if (end == ListEnd.Left)
                {
                    popped.Add(list.First!.Value);
                    list.RemoveFirst();
                }
                else
                {
                    popped.Add(list.Last!.Value);
                    list.RemoveLast();
                }
            }

            if (list.Count == 0)
            {
                Remove(key);
            }
            else if (popped.Count > 0)
            {
                Touch(key);
            }

            return popped;
        }
    }

    /// <summary>Returns the list length, or 0 if the key does not exist.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long ListLength(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<LinkedList<string>>(key)?.Count ?? 0;
        }
    }

    /// <summary>
    /// Returns elements between two inclusive indexes, using Redis <c>LRANGE</c> rules:
    /// negative indexes count from the end, and out-of-range indexes are clamped.
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<string> ListRange(string key, long start, long stop)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? list = GetLive<LinkedList<string>>(key);
            if (list is null)
            {
                return [];
            }

            long length = list.Count;
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
                return [];
            }

            return list.Skip((int)start).Take((int)(stop - start + 1)).ToList();
        }
    }

    // ───────────────────────────── Streams ─────────────────────────────

    /// <summary>
    /// Appends an entry (<c>XADD</c>), creating the stream if needed.
    /// </summary>
    /// <param name="key">The stream key.</param>
    /// <param name="id"><c>*</c>, <c>ms-*</c>, <c>ms-seq</c> or <c>ms</c>.</param>
    /// <param name="fields">Field/value pairs, kept in order.</param>
    /// <returns>The ID that was assigned.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">The ID is invalid or not greater than the current top ID.</exception>
    public StreamId StreamAdd(string key, string id, IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        lock (StripeFor(key))
        {
            StreamLog? log = GetLive<StreamLog>(key);
            StreamLog target = log ?? new StreamLog();

            // Resolve before creating the key, so a rejected ID leaves no empty stream behind.
            StreamId newId = target.ResolveNewId(id, NowMs);
            target.Append(new StreamEntry(newId, fields.ToArray()));

            if (log is null)
            {
                Store(key, target, NoExpiry);
            }
            else
            {
                Touch(key);
            }

            return newId;
        }
    }

    /// <summary>Entries with <paramref name="start"/> &lt;= ID &lt;= <paramref name="end"/> (<c>XRANGE</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<StreamEntry> StreamRange(string key, StreamId start, StreamId end, int? count = null)
    {
        lock (StripeFor(key))
        {
            return GetLive<StreamLog>(key)?.Range(start, end, count) ?? [];
        }
    }

    /// <summary>Entries with an ID strictly greater than <paramref name="after"/> (<c>XREAD</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<StreamEntry> StreamReadAfter(string key, StreamId after, int? count = null)
    {
        lock (StripeFor(key))
        {
            return GetLive<StreamLog>(key)?.ReadAfter(after, count) ?? [];
        }
    }

    /// <summary>The ID of the newest entry, or <see langword="null"/> if the stream does not exist.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public StreamId? StreamLastId(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<StreamLog>(key)?.LastId;
        }
    }

    /// <summary>Number of entries, or 0 if the stream does not exist.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long StreamLength(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<StreamLog>(key)?.Count ?? 0;
        }
    }

    // ───────────────────────────── Snapshots ─────────────────────────────

    /// <summary>
    /// Copies every live key. Each key is copied atomically; the snapshot as a whole is not
    /// a single point in time if writes happen concurrently.
    /// </summary>
    public IReadOnlyList<KeySnapshot> Snapshot()
    {
        var result = new List<KeySnapshot>(_entries.Count);
        foreach (string key in _entries.Keys)
        {
            lock (StripeFor(key))
            {
                Entry? entry = GetLive(key);
                if (entry is null)
                {
                    continue;
                }

                DateTimeOffset? expiresAt = entry.ExpiresAtMs == NoExpiry
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(entry.ExpiresAtMs);

                KeySnapshot? snapshot = entry.Value switch
                {
                    string text => new KeySnapshot(key, KeyType.String, text, expiresAt),
                    LinkedList<string> list => new KeySnapshot(key, KeyType.List, list.ToArray(), expiresAt),
                    StreamLog log => new KeySnapshot(key, KeyType.Stream, log.Entries.ToArray(), expiresAt),
                    Dictionary<string, string> hash => new KeySnapshot(key, KeyType.Hash, hash.ToArray(), expiresAt),
                    HashSet<string> set => new KeySnapshot(key, KeyType.Set, set.ToArray(), expiresAt),
                    SortedSetValue zset => new KeySnapshot(key, KeyType.SortedSet, zset.Ascending.ToArray(), expiresAt),
                    _ => null,
                };

                if (snapshot is not null)
                {
                    result.Add(snapshot);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Loads keys, replacing existing keys with the same name. Keys whose expiry has
    /// already passed are skipped.
    /// </summary>
    /// <returns>The number of keys loaded.</returns>
    public int Load(IEnumerable<KeySnapshot> keys)
    {
        long now = NowMs;
        int loaded = 0;

        foreach (KeySnapshot snapshot in keys)
        {
            long expiresAt = snapshot.ExpiresAt?.ToUnixTimeMilliseconds() ?? NoExpiry;
            if (expiresAt != NoExpiry && expiresAt <= now)
            {
                continue;
            }

            object value = snapshot.Type switch
            {
                KeyType.String => (string)snapshot.Value,
                KeyType.List => new LinkedList<string>((IEnumerable<string>)snapshot.Value),
                KeyType.Stream => new StreamLog((IEnumerable<StreamEntry>)snapshot.Value),
                KeyType.Hash => NewHash((IEnumerable<KeyValuePair<string, string>>)snapshot.Value),
                KeyType.Set => new HashSet<string>((IEnumerable<string>)snapshot.Value, StringComparer.Ordinal),
                KeyType.SortedSet => new SortedSetValue((IEnumerable<ScoredMember>)snapshot.Value),
                _ => throw new ArgumentException($"Cannot load a key of type {snapshot.Type}.", nameof(keys)),
            };

            lock (StripeFor(snapshot.Key))
            {
                Store(snapshot.Key, value, expiresAt);
            }

            loaded++;
        }

        return loaded;
    }

    /// <summary>Removes expired keys now. Returns how many were removed.</summary>
    public int EvictExpired()
    {
        long now = NowMs;
        int removed = 0;

        foreach ((string key, Entry entry) in _entries)
        {
            if (!entry.IsExpired(now))
            {
                continue;
            }

            lock (StripeFor(key))
            {
                // Re-check under the lock: the key may have been overwritten meanwhile.
                if (_entries.TryGetValue(key, out Entry? current) && current.IsExpired(NowMs) && Remove(key))
                {
                    removed++;
                }
            }
        }

        return removed;
    }

    /// <summary>Stops the background sweep.</summary>
    public void Dispose() => _evictionTimer?.Dispose();

    // ───────────────────────────── Internals ─────────────────────────────

    private object StripeFor(string key) => _stripes[StripeIndex(key)];

    private static int StripeIndex(string key) => (StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % StripeCount;

    /// <summary>
    /// Takes the stripes of every key in <paramref name="keys"/>, in ascending stripe order
    /// (so two multi-key operations can never deadlock). Dispose the result to release them.
    /// </summary>
    private StripeSet LockKeys(IEnumerable<string> keys)
    {
        int[] indexes = keys.Select(StripeIndex).Distinct().Order().ToArray();
        foreach (int index in indexes)
        {
            Monitor.Enter(_stripes[index]);
        }

        return new StripeSet(_stripes, indexes);
    }

    private long NextVersion() => Interlocked.Increment(ref _version);

    /// <summary>Stores a new value with a fresh version. Caller must hold the key's stripe.</summary>
    private void Store(string key, object value, long expiresAtMs) =>
        _entries[key] = new Entry(value, expiresAtMs, NextVersion());

    /// <summary>Records an in-place change to an entry. Caller must hold the key's stripe.</summary>
    private void Touch(Entry entry) => entry.Version = NextVersion();

    /// <summary>Records an in-place change to a key's value. Caller must hold the key's stripe.</summary>
    private void Touch(string key)
    {
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            Touch(entry);
        }
    }

    /// <summary>Removes a key. Caller must hold the key's stripe.</summary>
    private bool Remove(string key) => _entries.TryRemove(key, out _);

    private static Dictionary<string, string> NewHash(IEnumerable<KeyValuePair<string, string>> fields)
    {
        var hash = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string field, string value) in fields)
        {
            hash[field] = value;
        }

        return hash;
    }

    /// <summary>Returns the live entry, removing it if expired. Caller must hold the key's stripe.</summary>
    private Entry? GetLive(string key)
    {
        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            return null;
        }

        if (entry.IsExpired(NowMs))
        {
            Remove(key);
            return null;
        }

        return entry;
    }

    /// <summary>Returns the live value as <typeparamref name="T"/>. Caller must hold the key's stripe.</summary>
    private T? GetLive<T>(string key) where T : class
    {
        Entry? entry = GetLive(key);
        if (entry is null)
        {
            return null;
        }

        return entry.Value as T ?? throw new WrongTypeException();
    }

    private static long SaturatingAdd(long a, long b) => b > long.MaxValue - a ? long.MaxValue : a + b;

    private sealed class Entry(object value, long expiresAtMs, long version)
    {
        public object Value { get; set; } = value;

        /// <summary>Unix time in milliseconds, or <see cref="NoExpiry"/>.</summary>
        public long ExpiresAtMs { get; set; } = expiresAtMs;

        /// <summary>Changes whenever the key is modified.</summary>
        public long Version { get; set; } = version;

        public bool IsExpired(long nowMs) => ExpiresAtMs != NoExpiry && ExpiresAtMs <= nowMs;
    }

    /// <summary>Stripes held by <see cref="LockKeys"/>; releases them when disposed.</summary>
    private readonly struct StripeSet(object[] stripes, int[] indexes) : IDisposable
    {
        public void Dispose()
        {
            for (int i = indexes.Length - 1; i >= 0; i--)
            {
                Monitor.Exit(stripes[indexes[i]]);
            }
        }
    }
}
