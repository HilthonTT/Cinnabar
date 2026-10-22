namespace Cinnabar.Storage;

public sealed partial class Keyspace
{
    // ───────────────────────────── WATCH support ─────────────────────────────

    /// <summary>
    /// Starts watching <paramref name="key"/> and returns its current version. Pass the
    /// version to <see cref="IsUnchanged"/> later, and call <see cref="Unwatch"/> once per
    /// <see cref="Watch"/> when done.
    /// </summary>
    /// <remarks>
    /// Every write, TTL change, deletion and expiry of a key changes its version. While a key is
    /// watched, deleting it records a fresh "tombstone" version, so delete-then-recreate and
    /// create-then-delete are both detected.
    /// </remarks>
    public long Watch(string key)
    {
        lock (StripeFor(key))
        {
            _watchCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
            return CurrentVersion(key);
        }
    }

    /// <summary>Releases one <see cref="Watch"/> of <paramref name="key"/>.</summary>
    public void Unwatch(string key)
    {
        lock (StripeFor(key))
        {
            if (!_watchCounts.TryGetValue(key, out int count))
            {
                return;
            }

            if (count > 1)
            {
                _watchCounts[key] = count - 1;
            }
            else
            {
                _watchCounts.TryRemove(key, out _);
                _tombstones.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if the key has not changed since <see cref="Watch"/>
    /// returned <paramref name="version"/>. A key that expired meanwhile counts as changed.
    /// </summary>
    public bool IsUnchanged(string key, long version)
    {
        lock (StripeFor(key))
        {
            return CurrentVersion(key) == version;
        }
    }

    /// <summary>Number of keys with at least one watcher (for diagnostics and tests).</summary>
    public int WatchedKeyCount => _watchCounts.Count;

    /// <summary>The live entry's version, the tombstone's, or 0. Caller holds the stripe.</summary>
    private long CurrentVersion(string key) =>
        GetLive(key)?.Version ?? _tombstones.GetValueOrDefault(key);
}
