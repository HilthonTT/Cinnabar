using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>Formats sorted-set members as RESP.</summary>
internal static class SortedSetReplies
{
    /// <summary><c>[member, ...]</c>, or <c>[member, score, ...]</c> with scores.</summary>
    public static Reply Members(IEnumerable<ScoredMember> members, bool withScores) =>
        Reply.BulkStringArray(withScores
            ? members.SelectMany(m => new[] { m.Member, Arguments.Format(m.Score) })
            : members.Select(m => m.Member));

    /// <summary>A score as a bulk string, or nil.</summary>
    public static Reply Score(double? score) =>
        Reply.BulkString(score is { } value ? Arguments.Format(value) : null);
}
