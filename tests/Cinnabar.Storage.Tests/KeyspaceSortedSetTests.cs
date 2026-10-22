namespace Cinnabar.Storage.Tests;

public class KeyspaceSortedSetTests : KeyspaceTestBase
{
    private static readonly ScoreBound NegInf = new(double.NegativeInfinity, false);
    private static readonly ScoreBound PosInf = new(double.PositiveInfinity, false);

    private void Add(string key, params (double Score, string Member)[] members) =>
        Keyspace.SortedSetAdd(key, members.Select(m => new ScoredMember(m.Member, m.Score)).ToArray());

    private IEnumerable<string> All(string key) => Keyspace.SortedSetRangeByRank(key, 0, -1).Select(m => m.Member);

    [Fact]
    public void Members_are_ordered_by_score_then_bytes()
    {
        Add("z", (2, "b"), (1, "z"), (2, "a"), (-1, "neg"));

        All("z").Should().Equal("neg", "z", "a", "b");
        Keyspace.GetKeyType("z").Should().Be(KeyType.SortedSet);
    }

    [Fact]
    public void Add_reports_added_and_changed()
    {
        Add("z", (1, "a"), (2, "b"));

        SortedSetAddResult result = Keyspace.SortedSetAdd("z", [new("a", 1), new("b", 5), new("c", 3)]);

        result.Added.Should().Be(1);
        result.Changed.Should().Be(2);
        All("z").Should().Equal("a", "c", "b");
    }

    [Fact]
    public void Nx_only_adds_and_xx_only_updates()
    {
        Add("z", (1, "a"));

        Keyspace.SortedSetAdd("z", [new("a", 9), new("b", 2)], new SortedSetAddOptions(SetCondition.IfNotExists))
            .Added.Should().Be(1);
        Keyspace.SortedSetAdd("z", [new("a", 3), new("c", 2)], new SortedSetAddOptions(SetCondition.IfExists))
            .Changed.Should().Be(1);

        Keyspace.SortedSetScores("z", ["a", "b", "c"]).Should().Equal(3, 2, null);
    }

    [Fact]
    public void Gt_and_lt_only_move_scores_one_way_but_still_add()
    {
        Add("z", (5, "a"));

        Keyspace.SortedSetAdd("z", [new("a", 3), new("new", 1)], new SortedSetAddOptions(Comparison: ScoreComparison.GreaterThan));
        Keyspace.SortedSetScores("z", ["a", "new"]).Should().Equal(5, 1);

        Keyspace.SortedSetAdd("z", [new("a", 3)], new SortedSetAddOptions(Comparison: ScoreComparison.LessThan));
        Keyspace.SortedSetScores("z", ["a"]).Should().Equal(3);
    }

    [Fact]
    public void Increment_returns_the_new_score_or_null_when_prevented()
    {
        var incr = new SortedSetAddOptions(Increment: true);

        Keyspace.SortedSetAdd("z", [new("a", 2.5)], incr).Score.Should().Be(2.5);
        Keyspace.SortedSetAdd("z", [new("a", 1)], incr).Score.Should().Be(3.5);
        Keyspace.SortedSetAdd("z", [new("a", 1)], incr with { Condition = SetCondition.IfNotExists }).Score.Should().BeNull();
        Keyspace.SortedSetAdd("z", [new("a", -1)], incr with { Comparison = ScoreComparison.GreaterThan }).Score.Should().BeNull();
    }

    [Fact]
    public void Increment_to_nan_is_rejected()
    {
        Add("z", (double.PositiveInfinity, "a"));

        Action act = () => Keyspace.SortedSetAdd("z", [new("a", double.NegativeInfinity)], new SortedSetAddOptions(Increment: true));

        act.Should().Throw<StorageException>().WithMessage("ERR resulting score is not a number (NaN)");
    }

    [Fact]
    public void Removing_the_last_member_deletes_the_key()
    {
        Add("z", (1, "a"), (2, "b"));

        Keyspace.SortedSetRemove("z", ["a", "zz"]).Should().Be(1);
        Keyspace.SortedSetRemove("z", ["b"]).Should().Be(1);

        Keyspace.Exists("z").Should().BeFalse();
    }

    [Fact]
    public void Rank_in_both_directions()
    {
        Add("z", (10, "a"), (20, "b"), (30, "c"));

        Keyspace.SortedSetRank("z", "a", reverse: false).Should().Be((0L, 10.0));
        Keyspace.SortedSetRank("z", "c", reverse: false).Should().Be((2L, 30.0));
        Keyspace.SortedSetRank("z", "a", reverse: true).Should().Be((2L, 10.0));
        Keyspace.SortedSetRank("z", "zz", reverse: false).Should().BeNull();
        Keyspace.SortedSetRank("missing", "a", reverse: false).Should().BeNull();
    }

    [Theory]
    [InlineData(0, -1, new[] { "a", "b", "c", "d" })]
    [InlineData(1, 2, new[] { "b", "c" })]
    [InlineData(-2, -1, new[] { "c", "d" })]
    [InlineData(2, 100, new[] { "c", "d" })]
    [InlineData(3, 1, new string[0])]
    [InlineData(10, 20, new string[0])]
    public void Range_by_rank_uses_lrange_rules(long start, long stop, string[] expected)
    {
        Add("z", (1, "a"), (2, "b"), (3, "c"), (4, "d"));

        Keyspace.SortedSetRangeByRank("z", start, stop).Select(m => m.Member).Should().Equal(expected);
    }

    [Fact]
    public void Range_by_rank_reversed()
    {
        Add("z", (1, "a"), (2, "b"), (3, "c"));

        Keyspace.SortedSetRangeByRank("z", 0, 1, reverse: true).Select(m => m.Member).Should().Equal("c", "b");
    }

    [Fact]
    public void Range_by_score_honours_exclusive_bounds_and_infinity()
    {
        Add("z", (1, "a"), (2, "b"), (2, "b2"), (3, "c"), (double.PositiveInfinity, "inf"));

        Keyspace.SortedSetRangeByScore("z", new(2, false), new(3, false)).Select(m => m.Member).Should().Equal("b", "b2", "c");
        Keyspace.SortedSetRangeByScore("z", new(2, true), PosInf).Select(m => m.Member).Should().Equal("c", "inf");
        Keyspace.SortedSetRangeByScore("z", NegInf, new(2, true)).Select(m => m.Member).Should().Equal("a");
        Keyspace.SortedSetRangeByScore("z", new(3, false), new(2, false)).Should().BeEmpty();
    }

    [Fact]
    public void Range_by_score_reversed_with_limit()
    {
        Add("z", (1, "a"), (2, "b"), (3, "c"), (4, "d"));

        Keyspace.SortedSetRangeByScore("z", NegInf, PosInf, reverse: true, offset: 1, count: 2)
            .Select(m => m.Member).Should().Equal("c", "b");
        Keyspace.SortedSetRangeByScore("z", NegInf, PosInf, offset: 3, count: -1).Select(m => m.Member).Should().Equal("d");
        Keyspace.SortedSetRangeByScore("z", NegInf, PosInf, offset: -1, count: 5).Should().BeEmpty();
    }

    [Fact]
    public void Range_by_lex()
    {
        Add("z", (0, "a"), (0, "b"), (0, "c"), (0, "d"));
        LexBound.TryParse("[b", out LexBound fromB);
        LexBound.TryParse("(d", out LexBound beforeD);

        Keyspace.SortedSetRangeByLex("z", fromB, beforeD).Select(m => m.Member).Should().Equal("b", "c");
        Keyspace.SortedSetRangeByLex("z", LexBound.Minimum, LexBound.Maximum, reverse: true).Select(m => m.Member)
            .Should().Equal("d", "c", "b", "a");
        Keyspace.SortedSetLexCount("z", fromB, LexBound.Maximum).Should().Be(3);
    }

    [Fact]
    public void Count_by_score()
    {
        Add("z", (1, "a"), (2, "b"), (3, "c"));

        Keyspace.SortedSetCount("z", new(1, true), PosInf).Should().Be(2);
        Keyspace.SortedSetCount("missing", NegInf, PosInf).Should().Be(0);
    }

    [Fact]
    public void Pop_lowest_and_highest()
    {
        Add("z", (1, "a"), (2, "b"), (3, "c"));

        Keyspace.SortedSetPop("z", 2, highest: false).Should().Equal(new ScoredMember("a", 1), new ScoredMember("b", 2));
        Keyspace.SortedSetPop("z", 5, highest: true).Should().Equal(new ScoredMember("c", 3));

        Keyspace.Exists("z").Should().BeFalse();
    }

    [Fact]
    public void Remove_ranges()
    {
        Add("z", (1, "a"), (2, "b"), (3, "c"), (4, "d"), (5, "e"));

        Keyspace.SortedSetRemoveRangeByRank("z", 0, 0).Should().Be(1);
        Keyspace.SortedSetRemoveRangeByScore("z", new(4, false), PosInf).Should().Be(2);
        Keyspace.SortedSetRemoveRangeByLex("z", LexBound.Minimum, LexBound.Maximum).Should().Be(2);

        Keyspace.Exists("z").Should().BeFalse();
    }

    [Fact]
    public void Updating_a_score_moves_the_member()
    {
        Add("z", (1, "a"), (2, "b"));

        Add("z", (3, "a"));

        All("z").Should().Equal("b", "a");
        Keyspace.SortedSetCardinality("z").Should().Be(2);
    }

    [Fact]
    public void Large_set_stays_consistent()
    {
        var random = new Random(42);
        var expected = new Dictionary<string, double>();
        for (int i = 0; i < 2_000; i++)
        {
            string member = $"m{random.Next(500)}";
            double score = random.Next(100);
            expected[member] = score;
            Add("z", (score, member));
        }

        IReadOnlyList<ScoredMember> all = Keyspace.SortedSetRangeByRank("z", 0, -1);
        all.Should().HaveCount(expected.Count);
        all.Should().BeInAscendingOrder(m => m.Score);
        all.Should().AllSatisfy(m => m.Score.Should().Be(expected[m.Member]));
        Keyspace.SortedSetRank("z", all[123].Member, reverse: false)!.Value.Rank.Should().Be(123);
    }
}
