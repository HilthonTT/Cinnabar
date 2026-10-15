namespace Cinnabar.Storage;

/// <summary>
/// One entry of a stream: its ID and its field/value pairs in insertion order.
/// </summary>
public sealed record StreamEntry(StreamId Id, IReadOnlyList<KeyValuePair<string, string>> Fields);
