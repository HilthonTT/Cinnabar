namespace Cinnabar.Storage;

/// <summary>A copy of one pending entry, as part of a <see cref="StreamGroupSnapshot"/>.</summary>
/// <param name="Id">The entry ID.</param>
/// <param name="Consumer">The consumer that owns it.</param>
/// <param name="DeliveryTimeMs">When it was last delivered, in Unix milliseconds.</param>
/// <param name="DeliveryCount">How many times it has been delivered.</param>
public sealed record StreamPendingSnapshot(StreamId Id, string Consumer, long DeliveryTimeMs, long DeliveryCount);
