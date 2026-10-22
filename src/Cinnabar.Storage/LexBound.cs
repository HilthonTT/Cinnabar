namespace Cinnabar.Storage;

/// <summary>
/// One end of a lexicographic range, as in <c>ZRANGE key min max BYLEX</c>: <c>[a</c> is
/// inclusive, <c>(a</c> exclusive, <c>-</c> is below everything and <c>+</c> above everything.
/// Members compare byte by byte.
/// </summary>
public readonly record struct LexBound
{
    private LexBound(string? value, bool exclusive, int infinity)
    {
        Value = value;
        Exclusive = exclusive;
        Infinity = infinity;
    }

    /// <summary>The <c>-</c> bound.</summary>
    public static LexBound Minimum => new(null, false, -1);

    /// <summary>The <c>+</c> bound.</summary>
    public static LexBound Maximum => new(null, false, 1);

    /// <summary>The member to compare with, or <see langword="null"/> for <c>-</c> and <c>+</c>.</summary>
    public string? Value { get; }

    /// <summary>Whether <see cref="Value"/> itself is outside the range.</summary>
    public bool Exclusive { get; }

    /// <summary><c>-1</c> for <c>-</c>, <c>1</c> for <c>+</c>, otherwise 0.</summary>
    private int Infinity { get; }

    /// <summary>Parses <c>-</c>, <c>+</c>, <c>[member</c> or <c>(member</c>.</summary>
    public static bool TryParse(string text, out LexBound bound)
    {
        bound = text switch
        {
            "-" => Minimum,
            "+" => Maximum,
            ['[', .. var rest] => new LexBound(rest, false, 0),
            ['(', .. var rest] => new LexBound(rest, true, 0),
            _ => default,
        };

        return text is "-" or "+" || text.StartsWith('[') || text.StartsWith('(');
    }

    /// <summary>True if <paramref name="member"/> is on the inside of this lower bound.</summary>
    public bool AllowsAbove(string member) => Infinity switch
    {
        -1 => true,
        1 => false,
        _ => Compare(member) is var c && (Exclusive ? c > 0 : c >= 0),
    };

    /// <summary>True if <paramref name="member"/> is on the inside of this upper bound.</summary>
    public bool AllowsBelow(string member) => Infinity switch
    {
        -1 => false,
        1 => true,
        _ => Compare(member) is var c && (Exclusive ? c < 0 : c <= 0),
    };

    private int Compare(string member) => string.CompareOrdinal(member, Value);
}
