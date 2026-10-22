namespace Cinnabar.Storage;

/// <summary>Which existing scores <c>ZADD</c> may change.</summary>
public enum ScoreComparison
{
    /// <summary>Any change.</summary>
    Any,

    /// <summary>Only raise scores (<c>GT</c>).</summary>
    GreaterThan,

    /// <summary>Only lower scores (<c>LT</c>).</summary>
    LessThan,
}

/// <summary>The flags of <c>ZADD</c>.</summary>
/// <param name="Condition">Only add new members (<c>NX</c>) or only update existing ones (<c>XX</c>).</param>
/// <param name="Comparison">Only update when the new score is greater (<c>GT</c>) or less (<c>LT</c>).</param>
/// <param name="Increment">Add the given score to the current one (<c>INCR</c>); one member only.</param>
public sealed record SortedSetAddOptions(
    SetCondition Condition = SetCondition.Always,
    ScoreComparison Comparison = ScoreComparison.Any,
    bool Increment = false)
{
    /// <summary>Plain <c>ZADD</c>.</summary>
    public static SortedSetAddOptions Default { get; } = new();
}

/// <summary>The outcome of <c>ZADD</c>.</summary>
/// <param name="Added">Members that were new.</param>
/// <param name="Changed">Members that were new or whose score changed (what <c>CH</c> reports).</param>
/// <param name="Score">With <c>INCR</c>: the new score, or <see langword="null"/> if a flag prevented the update.</param>
public sealed record SortedSetAddResult(long Added, long Changed, double? Score);
