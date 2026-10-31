namespace Cinnabar.Client;

/// <summary>The result of XAUTOCLAIM.</summary>
/// <param name="NextStartId">Where to continue scanning; <c>0-0</c> once the whole PEL has been scanned.</param>
/// <param name="Entries">The entries claimed by the consumer.</param>
/// <param name="DeletedIds">Pending IDs whose entries no longer exist, which were removed from the PEL.</param>
public sealed record RedisAutoClaimResult(
    string NextStartId,
    IReadOnlyList<RedisStreamEntry> Entries,
    IReadOnlyList<string> DeletedIds);
