namespace Cinnabar.Client;

/// <summary>One pending entry, from the extended form of XPENDING.</summary>
/// <param name="Id">The entry ID.</param>
/// <param name="Consumer">The consumer it was last delivered to.</param>
/// <param name="Idle">Time since it was last delivered.</param>
/// <param name="DeliveryCount">How many times it has been delivered.</param>
public sealed record RedisPendingEntry(string Id, string Consumer, TimeSpan Idle, long DeliveryCount);
