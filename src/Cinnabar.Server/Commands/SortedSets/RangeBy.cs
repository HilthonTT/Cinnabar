namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>What the bounds of a ZRANGE-family command mean.</summary>
internal enum RangeBy
{
    /// <summary>0-based ranks.</summary>
    Rank,

    /// <summary>Scores (<c>BYSCORE</c>).</summary>
    Score,

    /// <summary>Members compared byte by byte (<c>BYLEX</c>).</summary>
    Lex,
}
