namespace Cinnabar.Client;

/// <summary>A stream entry returned by XRANGE or XREAD.</summary>
/// <param name="Id">The entry ID, <c>ms-seq</c>.</param>
/// <param name="Fields">Field/value pairs in insertion order.</param>
public sealed record RedisStreamEntry(string Id, IReadOnlyList<KeyValuePair<string, string>> Fields);
