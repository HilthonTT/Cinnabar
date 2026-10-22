namespace Cinnabar.Storage;

/// <summary>
/// One end of a score range, as in <c>ZRANGEBYSCORE key min max</c>: <c>1.5</c> is
/// inclusive, <c>(1.5</c> exclusive, and <c>-inf</c> / <c>+inf</c> are unbounded.
/// </summary>
public readonly record struct ScoreBound(double Value, bool Exclusive)
{
    /// <summary>Parses <c>[(]number</c> or <c>[(][+-]inf</c>.</summary>
    public static bool TryParse(string text, out ScoreBound bound)
    {
        bool exclusive = text.StartsWith('(');
        if (DoubleText.TryParse(exclusive ? text[1..] : text, out double value))
        {
            bound = new ScoreBound(value, exclusive);
            return true;
        }

        bound = default;
        return false;
    }

    /// <summary>True if <paramref name="score"/> is on the inside of this lower bound.</summary>
    public bool AllowsAbove(double score) => Exclusive ? score > Value : score >= Value;

    /// <summary>True if <paramref name="score"/> is on the inside of this upper bound.</summary>
    public bool AllowsBelow(double score) => Exclusive ? score < Value : score <= Value;
}
