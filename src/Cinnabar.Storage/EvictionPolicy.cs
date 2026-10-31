namespace Cinnabar.Storage;

/// <summary>Which keys to remove when the keyspace is over its memory limit (<c>maxmemory-policy</c>).</summary>
public enum EvictionPolicy
{
    /// <summary>Evict nothing; writes that need memory fail with <c>OOM</c> instead (<c>noeviction</c>).</summary>
    NoEviction,

    /// <summary>Evict the least recently used key, approximated by sampling (<c>allkeys-lru</c>).</summary>
    AllKeysLru,

    /// <summary>Evict the least recently used key that has a TTL (<c>volatile-lru</c>).</summary>
    VolatileLru,

    /// <summary>Evict a random key (<c>allkeys-random</c>).</summary>
    AllKeysRandom,

    /// <summary>Evict a random key that has a TTL (<c>volatile-random</c>).</summary>
    VolatileRandom,

    /// <summary>Evict the key with a TTL that expires soonest, approximated by sampling (<c>volatile-ttl</c>).</summary>
    VolatileTtl,
}
