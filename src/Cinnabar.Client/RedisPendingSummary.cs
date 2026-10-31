namespace Cinnabar.Client;

/// <summary>The summary form of XPENDING: what a consumer group has delivered but not acknowledged.</summary>
/// <param name="Count">Number of pending entries.</param>
/// <param name="SmallestId">The lowest pending ID, or <see langword="null"/> if nothing is pending.</param>
/// <param name="LargestId">The highest pending ID, or <see langword="null"/> if nothing is pending.</param>
/// <param name="Consumers">Pending entries per consumer, for consumers that have any.</param>
public sealed record RedisPendingSummary(
    long Count,
    string? SmallestId,
    string? LargestId,
    IReadOnlyList<KeyValuePair<string, long>> Consumers);
