using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>
/// A parsed ZRANGE-family query. Every variant (ZRANGE, ZREVRANGE, ZRANGEBYSCORE, ...) is
/// turned into one of these and run the same way.
/// </summary>
/// <param name="Key">The sorted set.</param>
/// <param name="Start">The first bound as given: the lower one, or the upper one when reversed by score or lex.</param>
/// <param name="Stop">The second bound as given.</param>
/// <param name="By">What the bounds mean.</param>
/// <param name="Reverse">Highest scores first.</param>
/// <param name="Limit"><c>LIMIT offset count</c>, if given; a negative count means no limit.</param>
/// <param name="WithScores">Include scores in the reply.</param>
internal sealed record RangeQuery(
    string Key,
    string Start,
    string Stop,
    RangeBy By,
    bool Reverse = false,
    (long Offset, long Count)? Limit = null,
    bool WithScores = false)
{
    /// <summary>
    /// Parses options from <paramref name="index"/> on. <paramref name="allowed"/> lists the
    /// accepted keywords among BYSCORE, BYLEX, REV, LIMIT and WITHSCORES.
    /// </summary>
    public static RangeQuery Parse(Command command, int index, RangeQuery query, params string[] allowed)
    {
        for (; index < command.Count; index++)
        {
            string option = command[index].ToUpperInvariant();
            if (!allowed.Contains(option))
            {
                throw new StorageException(Arguments.SyntaxError);
            }

            switch (option)
            {
                case "BYSCORE":
                    query = query with { By = RangeBy.Score };
                    break;
                case "BYLEX":
                    query = query with { By = RangeBy.Lex };
                    break;
                case "REV":
                    query = query with { Reverse = true };
                    break;
                case "WITHSCORES":
                    query = query with { WithScores = true };
                    break;
                case "LIMIT" when index + 2 < command.Count:
                    query = query with { Limit = (Arguments.ParseLong(command[index + 1]), Arguments.ParseLong(command[index + 2])) };
                    index += 2;
                    break;
                default:
                    throw new StorageException(Arguments.SyntaxError);
            }
        }

        return query;
    }

    /// <summary>Runs the query and formats the reply.</summary>
    public Reply Execute(Keyspace keyspace)
    {
        if (Limit is not null && By == RangeBy.Rank)
        {
            throw new StorageException("ERR syntax error, LIMIT is only supported in combination with either BYSCORE or BYLEX");
        }

        if (WithScores && By == RangeBy.Lex)
        {
            throw new StorageException("ERR syntax error, WITHSCORES not supported in combination with BYLEX");
        }

        // Reversed score and lex ranges name the upper bound first.
        (string low, string high) = Reverse ? (Stop, Start) : (Start, Stop);
        (long offset, long count) = Limit ?? (0, -1);

        IReadOnlyList<ScoredMember> members = By switch
        {
            RangeBy.Rank => keyspace.SortedSetRangeByRank(Key, Arguments.ParseLong(Start), Arguments.ParseLong(Stop), Reverse),
            RangeBy.Score => keyspace.SortedSetRangeByScore(
                Key, Arguments.ParseScoreBound(low), Arguments.ParseScoreBound(high), Reverse, offset, count),
            _ => keyspace.SortedSetRangeByLex(
                Key, Arguments.ParseLexBound(low), Arguments.ParseLexBound(high), Reverse, offset, count),
        };

        return SortedSetReplies.Members(members, WithScores);
    }
}
