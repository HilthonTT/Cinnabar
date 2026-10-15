using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Cinnabar.Storage;

/// <summary>
/// A stream entry ID: <c>&lt;milliseconds&gt;-&lt;sequence&gt;</c>. IDs are ordered by
/// milliseconds first, then sequence.
/// </summary>
public readonly record struct StreamId(ulong Milliseconds, ulong Sequence) : IComparable<StreamId>
{
    /// <summary>The smallest possible ID, <c>0-0</c>.</summary>
    public static readonly StreamId Min = new(0, 0);

    /// <summary>The largest possible ID.</summary>
    public static readonly StreamId Max = new(ulong.MaxValue, ulong.MaxValue);

    /// <inheritdoc />
    public int CompareTo(StreamId other)
    {
        int byTime = Milliseconds.CompareTo(other.Milliseconds);
        return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
    }

    /// <summary>Returns <c>ms-seq</c>.</summary>
    public override string ToString() =>
        $"{Milliseconds.ToString(CultureInfo.InvariantCulture)}-{Sequence.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The ID immediately after this one, or <see langword="null"/> if this is <see cref="Max"/>.</summary>
    public StreamId? Next() => this == Max
        ? null
        : Sequence == ulong.MaxValue ? new StreamId(Milliseconds + 1, 0) : this with { Sequence = Sequence + 1 };

    /// <summary>The ID immediately before this one, or <see langword="null"/> if this is <see cref="Min"/>.</summary>
    public StreamId? Previous() => this == Min
        ? null
        : Sequence == 0 ? new StreamId(Milliseconds - 1, ulong.MaxValue) : this with { Sequence = Sequence - 1 };

    /// <summary>
    /// Parses <c>ms-seq</c>, or a bare <c>ms</c> whose sequence defaults to <paramref name="defaultSequence"/>.
    /// </summary>
    public static bool TryParse(string text, ulong defaultSequence, out StreamId id)
    {
        id = default;
        int dash = text.IndexOf('-');
        string msPart = dash < 0 ? text : text[..dash];

        if (!ulong.TryParse(msPart, NumberStyles.None, CultureInfo.InvariantCulture, out ulong ms))
        {
            return false;
        }

        ulong seq = defaultSequence;
        if (dash >= 0 && !ulong.TryParse(text[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out seq))
        {
            return false;
        }

        id = new StreamId(ms, seq);
        return true;
    }

    /// <summary>
    /// Parses a range bound as used by XRANGE: <c>-</c>, <c>+</c>, an ID, or a bare
    /// millisecond value, optionally prefixed by <c>(</c> for an exclusive bound.
    /// </summary>
    /// <returns><see langword="false"/> if the text is malformed.</returns>
    public static bool TryParseRangeBound(string text, bool isStart, [NotNullWhen(true)] out StreamId? id)
    {
        id = null;
        bool exclusive = text.StartsWith('(');
        string body = exclusive ? text[1..] : text;

        StreamId bound;
        if (body == "-")
        {
            bound = Min;
        }
        else if (body == "+")
        {
            bound = Max;
        }
        else if (!TryParse(body, isStart ? 0 : ulong.MaxValue, out bound))
        {
            return false;
        }

        if (!exclusive)
        {
            id = bound;
            return true;
        }

        // "(id" as a start means "after id"; as an end it means "before id".
        id = isStart ? bound.Next() ?? Max : bound.Previous() ?? Min;
        return true;
    }
}
