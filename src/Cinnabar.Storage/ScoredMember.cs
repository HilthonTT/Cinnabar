namespace Cinnabar.Storage;

/// <summary>A sorted-set member and its score.</summary>
public readonly record struct ScoredMember(string Member, double Score);
