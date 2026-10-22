namespace Cinnabar.Storage;

/// <summary>
/// The value of a sorted-set key: unique members, each with a score, ordered by score and
/// then by member (byte order).
/// </summary>
/// <remarks>
/// A dictionary gives O(1) score lookup; a red-black tree (<see cref="SortedSet{T}"/>) keeps
/// the order. Adding, removing and score-range scans cost O(log n) plus the size of the
/// result. Rank lookups and rank ranges walk the tree from one end, so they cost O(rank);
/// Redis uses a skip list with span counts to make those O(log n) as well. Not thread-safe:
/// <see cref="Keyspace"/> guards it with the key's lock.
/// </remarks>
internal sealed class SortedSetValue
{
    // Sentinel members used only as range ends: null sorts below every member at a score,
    // and Top (compared by reference) above every member.
    private static readonly string Top = new('￿', 1);

    private readonly Dictionary<string, double> _scores = new(StringComparer.Ordinal);
    private readonly SortedSet<ScoredMember> _ordered = new(OrderComparer.Instance);

    public SortedSetValue()
    {
    }

    public SortedSetValue(IEnumerable<ScoredMember> members)
    {
        foreach (ScoredMember member in members)
        {
            Set(member.Member, member.Score);
        }
    }

    public int Count => _scores.Count;

    /// <summary>Members from the lowest score to the highest.</summary>
    public IEnumerable<ScoredMember> Ascending => _ordered;

    /// <summary>Members from the highest score to the lowest.</summary>
    public IEnumerable<ScoredMember> Descending => _ordered.Reverse();

    public bool TryGetScore(string member, out double score) => _scores.TryGetValue(member, out score);

    /// <summary>Adds the member or changes its score.</summary>
    public void Set(string member, double score)
    {
        if (_scores.TryGetValue(member, out double old))
        {
            if (old.Equals(score))
            {
                return;
            }

            _ordered.Remove(new ScoredMember(member, old));
        }

        _scores[member] = score;
        _ordered.Add(new ScoredMember(member, score));
    }

    public bool Remove(string member)
    {
        if (!_scores.Remove(member, out double score))
        {
            return false;
        }

        _ordered.Remove(new ScoredMember(member, score));
        return true;
    }

    /// <summary>The 0-based position of <paramref name="member"/>, or <see langword="null"/> if absent.</summary>
    public long? Rank(string member, bool reverse)
    {
        if (!_scores.TryGetValue(member, out double score))
        {
            return null;
        }

        int below = _ordered.GetViewBetween(_ordered.Min, new ScoredMember(member, score)).Count - 1;
        return reverse ? Count - 1 - below : below;
    }

    /// <summary>Members whose score lies between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public IEnumerable<ScoredMember> ScoreRange(ScoreBound min, ScoreBound max, bool reverse)
    {
        if (Count == 0 || min.Value > max.Value)
        {
            return [];
        }

        SortedSet<ScoredMember> view = _ordered.GetViewBetween(
            new ScoredMember(null!, min.Value),
            new ScoredMember(Top, max.Value));

        IEnumerable<ScoredMember> ordered = reverse ? view.Reverse() : view;
        return ordered.Where(m => min.AllowsAbove(m.Score) && max.AllowsBelow(m.Score));
    }

    /// <summary>Members between two lexicographic bounds, in set order.</summary>
    public IEnumerable<ScoredMember> LexRange(LexBound min, LexBound max, bool reverse) =>
        (reverse ? Descending : Ascending).Where(m => min.AllowsAbove(m.Member) && max.AllowsBelow(m.Member));

    /// <summary>Orders by score, then by member in byte order.</summary>
    private sealed class OrderComparer : IComparer<ScoredMember>
    {
        public static readonly OrderComparer Instance = new();

        public int Compare(ScoredMember x, ScoredMember y)
        {
            int byScore = x.Score.CompareTo(y.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            if (ReferenceEquals(x.Member, y.Member))
            {
                return 0;
            }

            if (x.Member is null || ReferenceEquals(y.Member, Top))
            {
                return -1;
            }

            if (y.Member is null || ReferenceEquals(x.Member, Top))
            {
                return 1;
            }

            return string.CompareOrdinal(x.Member, y.Member);
        }
    }
}
