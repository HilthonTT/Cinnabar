using System.Diagnostics.CodeAnalysis;

namespace Cinnabar.Storage;

/// <summary>Converts <see cref="EvictionPolicy"/> to and from the names Redis uses (<c>allkeys-lru</c>, ...).</summary>
public static class EvictionPolicyNames
{
    private static readonly (EvictionPolicy Policy, string Name)[] Names =
    [
        (EvictionPolicy.NoEviction, "noeviction"),
        (EvictionPolicy.AllKeysLru, "allkeys-lru"),
        (EvictionPolicy.VolatileLru, "volatile-lru"),
        (EvictionPolicy.AllKeysRandom, "allkeys-random"),
        (EvictionPolicy.VolatileRandom, "volatile-random"),
        (EvictionPolicy.VolatileTtl, "volatile-ttl"),
    ];

    /// <summary>Every supported name, for help and error messages.</summary>
    public static IEnumerable<string> All => Names.Select(n => n.Name);

    /// <summary>The Redis name of <paramref name="policy"/>.</summary>
    public static string ToName(EvictionPolicy policy) => Names.First(n => n.Policy == policy).Name;

    /// <summary>Parses a Redis policy name, ignoring case.</summary>
    public static bool TryParse(string? name, [NotNullWhen(true)] out EvictionPolicy? policy)
    {
        foreach ((EvictionPolicy candidate, string candidateName) in Names)
        {
            if (string.Equals(name, candidateName, StringComparison.OrdinalIgnoreCase))
            {
                policy = candidate;
                return true;
            }
        }

        policy = null;
        return false;
    }
}
