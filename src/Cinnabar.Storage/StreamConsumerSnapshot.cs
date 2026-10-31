namespace Cinnabar.Storage;

/// <summary>A copy of one consumer, as part of a <see cref="StreamGroupSnapshot"/>.</summary>
/// <param name="Name">The consumer name.</param>
/// <param name="SeenTimeMs">When the consumer last ran a command, in Unix milliseconds.</param>
public sealed record StreamConsumerSnapshot(string Name, long SeenTimeMs);
