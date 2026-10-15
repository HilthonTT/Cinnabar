namespace Cinnabar.Storage;

/// <summary>
/// A point-in-time copy of one key, used to write and load snapshots.
/// <see cref="Value"/> is a <see cref="string"/> for <see cref="KeyType.String"/>, an
/// <see cref="IReadOnlyList{T}"/> of strings for <see cref="KeyType.List"/>, and an
/// <see cref="IReadOnlyList{T}"/> of <see cref="StreamEntry"/> for <see cref="KeyType.Stream"/>.
/// </summary>
public sealed record KeySnapshot(string Key, KeyType Type, object Value, DateTimeOffset? ExpiresAt);
