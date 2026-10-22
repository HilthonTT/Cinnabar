namespace Cinnabar.Storage;

/// <summary>
/// A point-in-time copy of one key, used to write and load snapshots.
/// </summary>
/// <remarks>
/// <see cref="Value"/> depends on <see cref="Type"/>:
/// <list type="bullet">
///   <item><see cref="KeyType.String"/>: a <see cref="string"/>.</item>
///   <item><see cref="KeyType.List"/> and <see cref="KeyType.Set"/>: an <see cref="IReadOnlyList{T}"/> of strings.</item>
///   <item><see cref="KeyType.Stream"/>: an <see cref="IReadOnlyList{T}"/> of <see cref="StreamEntry"/>.</item>
///   <item><see cref="KeyType.Hash"/>: an <see cref="IReadOnlyList{T}"/> of field/value <see cref="KeyValuePair{TKey,TValue}"/>s.</item>
///   <item><see cref="KeyType.SortedSet"/>: an <see cref="IReadOnlyList{T}"/> of <see cref="ScoredMember"/>, in score order.</item>
/// </list>
/// </remarks>
public sealed record KeySnapshot(string Key, KeyType Type, object Value, DateTimeOffset? ExpiresAt);
