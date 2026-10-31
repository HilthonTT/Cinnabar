using System.Globalization;
using Cinnabar.Storage;

namespace Cinnabar.Server.Configuration;

/// <summary>Memory limit and eviction (<c>maxmemory</c>, <c>maxmemory-policy</c>, <c>maxmemory-samples</c>).</summary>
public sealed class MemorySettings
{
    /// <summary>
    /// The memory limit for stored data, as bytes or with a unit: <c>100mb</c>, <c>1gb</c>,
    /// <c>512kb</c> (units are powers of 1024, as in Redis). <c>0</c> means no limit.
    /// </summary>
    /// <remarks>
    /// The limit applies to Cinnabar's estimate of the data's size
    /// (<see cref="Keyspace.MemoryUsage"/>), not to the process's total memory.
    /// </remarks>
    public string MaxMemory { get; set; } = "0";

    /// <summary>
    /// What to do when a write arrives while over the limit: <c>noeviction</c> (refuse it with
    /// <c>OOM</c>), <c>allkeys-lru</c>, <c>volatile-lru</c>, <c>allkeys-random</c>,
    /// <c>volatile-random</c> or <c>volatile-ttl</c>.
    /// </summary>
    public string MaxMemoryPolicy { get; set; } = "noeviction";

    /// <summary>Keys sampled per eviction. More samples approximate true LRU/TTL order more closely.</summary>
    public int MaxMemorySamples { get; set; } = 5;

    /// <summary><see cref="MaxMemory"/> in bytes; 0 means no limit.</summary>
    /// <exception cref="ArgumentException"><see cref="MaxMemory"/> is not a valid size.</exception>
    public long MaxMemoryBytes => ParseSize(MaxMemory);

    /// <summary><see cref="MaxMemoryPolicy"/> parsed.</summary>
    /// <exception cref="ArgumentException"><see cref="MaxMemoryPolicy"/> is not a supported policy.</exception>
    public EvictionPolicy Policy =>
        EvictionPolicyNames.TryParse(MaxMemoryPolicy, out EvictionPolicy? policy)
            ? policy.Value
            : throw new ArgumentException(
                $"Invalid maxmemory policy '{MaxMemoryPolicy}': expected one of {string.Join(", ", EvictionPolicyNames.All)}.");

    /// <summary>Checks every value, so a bad setting fails at startup rather than on the first write.</summary>
    /// <exception cref="ArgumentException">A value is invalid.</exception>
    public void Validate()
    {
        _ = MaxMemoryBytes;
        _ = Policy;
        if (MaxMemorySamples < 1)
        {
            throw new ArgumentException($"Invalid maxmemory samples {MaxMemorySamples}: expected an integer >= 1.");
        }
    }

    /// <summary>Parses a size such as <c>1048576</c>, <c>100mb</c> or <c>2GB</c>.</summary>
    /// <exception cref="ArgumentException">The text is not a non-negative size.</exception>
    public static long ParseSize(string text)
    {
        string trimmed = text.Trim().ToLowerInvariant();
        (string Suffix, long Multiplier)[] units = [("kb", 1L << 10), ("mb", 1L << 20), ("gb", 1L << 30), ("k", 1000), ("m", 1_000_000), ("g", 1_000_000_000), ("b", 1)];

        long multiplier = 1;
        foreach ((string suffix, long unit) in units)
        {
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
            {
                trimmed = trimmed[..^suffix.Length];
                multiplier = unit;
                break;
            }
        }

        if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
        {
            throw new ArgumentException($"Invalid memory size '{text}': expected bytes or a number with kb, mb or gb.");
        }

        try
        {
            return checked(value * multiplier);
        }
        catch (OverflowException)
        {
            throw new ArgumentException($"Invalid memory size '{text}': too large.");
        }
    }
}
