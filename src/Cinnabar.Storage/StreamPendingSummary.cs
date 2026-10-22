namespace Cinnabar.Storage;

/// <summary>The summary form of <c>XPENDING key group</c>.</summary>
/// <param name="Count">Number of pending entries.</param>
/// <param name="Smallest">The lowest pending ID, if any.</param>
/// <param name="Largest">The highest pending ID, if any.</param>
/// <param name="Consumers">Each consumer with pending entries, and how many it has.</param>
public sealed record StreamPendingSummary(
    long Count,
    StreamId? Smallest,
    StreamId? Largest,
    IReadOnlyList<KeyValuePair<string, long>> Consumers);
