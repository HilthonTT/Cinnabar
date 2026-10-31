namespace Cinnabar.Client;

/// <summary>The entries one stream returned to XREAD or XREADGROUP.</summary>
/// <param name="Key">The stream key.</param>
/// <param name="Entries">The entries, oldest first.</param>
public sealed record RedisStreamRead(string Key, IReadOnlyList<RedisStreamEntry> Entries);
