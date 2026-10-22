namespace Cinnabar.Storage;

/// <summary>One delivered but unacknowledged entry, as listed by <c>XPENDING key group start end count</c>.</summary>
/// <param name="Id">The entry ID.</param>
/// <param name="Consumer">The consumer that owns it.</param>
/// <param name="IdleMilliseconds">Time since it was last delivered.</param>
/// <param name="DeliveryCount">How many times it has been delivered.</param>
public sealed record StreamPendingEntry(StreamId Id, string Consumer, long IdleMilliseconds, long DeliveryCount);
