namespace Cinnabar.Client;

/// <summary>A sorted-set member and its score.</summary>
/// <param name="Member">The member.</param>
/// <param name="Score">Its score; may be infinite.</param>
public sealed record RedisSortedSetEntry(string Member, double Score);
