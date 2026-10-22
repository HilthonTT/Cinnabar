namespace Cinnabar.Storage;

public sealed partial class Keyspace
{
    // ───────────────────────────── Sets ─────────────────────────────

    /// <summary>Adds members (<c>SADD</c>), creating the set if needed.</summary>
    /// <returns>The number of members that were not already present.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SetAdd(string key, IEnumerable<string> members)
    {
        lock (StripeFor(key))
        {
            HashSet<string>? existing = GetLive<HashSet<string>>(key);
            HashSet<string> set = existing ?? new HashSet<string>(StringComparer.Ordinal);

            long added = members.Count(set.Add);
            if (existing is null)
            {
                if (set.Count > 0)
                {
                    Store(key, set, NoExpiry);
                }
            }
            else if (added > 0)
            {
                Touch(key);
            }

            return added;
        }
    }

    /// <summary>Removes members (<c>SREM</c>). A set that becomes empty is deleted.</summary>
    /// <returns>The number of members removed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SetRemove(string key, IEnumerable<string> members)
    {
        lock (StripeFor(key))
        {
            HashSet<string>? set = GetLive<HashSet<string>>(key);
            if (set is null)
            {
                return 0;
            }

            long removed = members.Count(set.Remove);
            RemoveIfEmptyOrTouch(key, set.Count, removed > 0);
            return removed;
        }
    }

    /// <summary>Whether each member is in the set (<c>SISMEMBER</c>, <c>SMISMEMBER</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<bool> SetContains(string key, IReadOnlyList<string> members)
    {
        lock (StripeFor(key))
        {
            HashSet<string>? set = GetLive<HashSet<string>>(key);
            return members.Select(member => set?.Contains(member) ?? false).ToArray();
        }
    }

    /// <summary>Every member, in no particular order (<c>SMEMBERS</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<string> SetMembers(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<HashSet<string>>(key)?.ToArray() ?? [];
        }
    }

    /// <summary>Number of members, or 0 if the key does not exist (<c>SCARD</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long SetCardinality(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<HashSet<string>>(key)?.Count ?? 0;
        }
    }

    /// <summary>Removes and returns up to <paramref name="count"/> random members (<c>SPOP</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<string> SetPop(string key, int count)
    {
        lock (StripeFor(key))
        {
            HashSet<string>? set = GetLive<HashSet<string>>(key);
            if (set is null || count <= 0)
            {
                return [];
            }

            string[] popped = PickDistinct(set, count);
            foreach (string member in popped)
            {
                set.Remove(member);
            }

            RemoveIfEmptyOrTouch(key, set.Count, popped.Length > 0);
            return popped;
        }
    }

    /// <summary>
    /// Random members without removing them (<c>SRANDMEMBER</c>). A positive
    /// <paramref name="count"/> returns up to that many distinct members; a negative one
    /// returns exactly <c>|count|</c> members that may repeat.
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<string> SetRandomMembers(string key, long count)
    {
        lock (StripeFor(key))
        {
            HashSet<string>? set = GetLive<HashSet<string>>(key);
            if (set is null || count == 0)
            {
                return [];
            }

            if (count > 0)
            {
                return PickDistinct(set, (int)Math.Min(count, set.Count));
            }

            string[] members = [.. set];
            var picked = new string[checked((int)-count)];
            for (int i = 0; i < picked.Length; i++)
            {
                picked[i] = members[Random.Shared.Next(members.Length)];
            }

            return picked;
        }
    }

    /// <summary>
    /// Moves a member from one set to another atomically (<c>SMOVE</c>).
    /// </summary>
    /// <returns><see langword="true"/> if the member was in <paramref name="source"/>.</returns>
    /// <exception cref="WrongTypeException">Either key holds another type.</exception>
    public bool SetMove(string source, string destination, string member)
    {
        using StripeSet locks = LockKeys([source, destination]);

        HashSet<string>? from = GetLive<HashSet<string>>(source);
        HashSet<string>? to = GetLive<HashSet<string>>(destination);
        if (from is null || !from.Contains(member))
        {
            return false;
        }

        if (source == destination)
        {
            return true;
        }

        from.Remove(member);
        RemoveIfEmptyOrTouch(source, from.Count, changed: true);

        if (to is null)
        {
            Store(destination, new HashSet<string>(StringComparer.Ordinal) { member }, NoExpiry);
        }
        else if (to.Add(member))
        {
            Touch(destination);
        }

        return true;
    }

    /// <summary>
    /// Combines sets atomically (<c>SINTER</c>, <c>SUNION</c>, <c>SDIFF</c>). Missing keys are
    /// empty sets.
    /// </summary>
    /// <exception cref="WrongTypeException">A key holds another type.</exception>
    public IReadOnlyList<string> SetCombine(SetOperation operation, IReadOnlyList<string> keys)
    {
        using StripeSet locks = LockKeys(keys);
        return [.. Combine(operation, keys)];
    }

    /// <summary>
    /// Combines sets and stores the result in <paramref name="destination"/>, replacing any
    /// value there (<c>SINTERSTORE</c>, <c>SUNIONSTORE</c>, <c>SDIFFSTORE</c>). An empty result
    /// deletes the destination.
    /// </summary>
    /// <returns>The size of the result.</returns>
    /// <exception cref="WrongTypeException">A source key holds another type.</exception>
    public long SetCombineAndStore(SetOperation operation, string destination, IReadOnlyList<string> keys)
    {
        using StripeSet locks = LockKeys([destination, .. keys]);

        HashSet<string> result = Combine(operation, keys);
        if (result.Count == 0)
        {
            if (GetLive(destination) is not null)
            {
                Remove(destination);
            }
        }
        else
        {
            Store(destination, result, NoExpiry);
        }

        return result.Count;
    }

    /// <summary>
    /// Size of the intersection, stopping early once it reaches <paramref name="limit"/>
    /// (0 means no limit) (<c>SINTERCARD</c>).
    /// </summary>
    /// <exception cref="WrongTypeException">A key holds another type.</exception>
    public long SetIntersectionCardinality(IReadOnlyList<string> keys, long limit)
    {
        using StripeSet locks = LockKeys(keys);

        HashSet<string>?[] sets = ReadSets(keys);
        if (sets.Any(set => set is null))
        {
            return 0;
        }

        HashSet<string>[] bySize = [.. sets.Select(set => set!).OrderBy(set => set.Count)];
        long count = 0;
        foreach (string member in bySize[0])
        {
            if (bySize.Skip(1).All(set => set.Contains(member)) && ++count == limit)
            {
                break;
            }
        }

        return count;
    }

    /// <summary>Computes a set operation. Caller holds every key's stripe.</summary>
    private HashSet<string> Combine(SetOperation operation, IReadOnlyList<string> keys)
    {
        HashSet<string>?[] sets = ReadSets(keys);
        switch (operation)
        {
            case SetOperation.Union:
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach (HashSet<string>? set in sets)
                {
                    if (set is not null)
                    {
                        result.UnionWith(set);
                    }
                }

                return result;
            }

            case SetOperation.Intersect:
            {
                if (sets.Any(set => set is null))
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }

                // Start from the smallest set so the result never grows past it.
                HashSet<string>[] bySize = [.. sets.Select(set => set!).OrderBy(set => set.Count)];
                var result = new HashSet<string>(bySize[0], StringComparer.Ordinal);
                foreach (HashSet<string> set in bySize.Skip(1))
                {
                    result.IntersectWith(set);
                }

                return result;
            }

            default:
            {
                var result = new HashSet<string>(sets[0] ?? [], StringComparer.Ordinal);
                foreach (HashSet<string>? set in sets.Skip(1))
                {
                    if (set is not null)
                    {
                        result.ExceptWith(set);
                    }
                }

                return result;
            }
        }
    }

    /// <summary>Reads every key as a set (null when missing). Caller holds every key's stripe.</summary>
    private HashSet<string>?[] ReadSets(IReadOnlyList<string> keys) =>
        keys.Select(GetLive<HashSet<string>>).ToArray();

    /// <summary>Picks <paramref name="count"/> distinct members at random (fewer if the set is smaller).</summary>
    private static string[] PickDistinct(HashSet<string> set, int count)
    {
        string[] members = [.. set];
        if (count >= members.Length)
        {
            return members;
        }

        // Partial Fisher–Yates shuffle: the first `count` slots end up a uniform random sample.
        for (int i = 0; i < count; i++)
        {
            int j = Random.Shared.Next(i, members.Length);
            (members[i], members[j]) = (members[j], members[i]);
        }

        return members[..count];
    }
}
