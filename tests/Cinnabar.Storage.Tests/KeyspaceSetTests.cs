namespace Cinnabar.Storage.Tests;

public class KeyspaceSetTests : KeyspaceTestBase
{
    [Fact]
    public void Add_counts_only_new_members()
    {
        Keyspace.SetAdd("s", ["a", "b", "a"]).Should().Be(2);
        Keyspace.SetAdd("s", ["b", "c"]).Should().Be(1);

        Keyspace.SetMembers("s").Should().BeEquivalentTo(["a", "b", "c"]);
        Keyspace.SetCardinality("s").Should().Be(3);
        Keyspace.GetKeyType("s").Should().Be(KeyType.Set);
    }

    [Fact]
    public void Members_are_compared_byte_for_byte()
    {
        Keyspace.SetAdd("s", ["a", "A", "a "]).Should().Be(3);
    }

    [Fact]
    public void Removing_the_last_member_deletes_the_key()
    {
        Keyspace.SetAdd("s", ["a", "b"]);

        Keyspace.SetRemove("s", ["a", "zz"]).Should().Be(1);
        Keyspace.SetRemove("s", ["b"]).Should().Be(1);

        Keyspace.Exists("s").Should().BeFalse();
    }

    [Fact]
    public void Contains_checks_each_member()
    {
        Keyspace.SetAdd("s", ["a"]);

        Keyspace.SetContains("s", ["a", "b"]).Should().Equal(true, false);
        Keyspace.SetContains("missing", ["a"]).Should().Equal(false);
    }

    [Fact]
    public void Pop_removes_distinct_random_members()
    {
        Keyspace.SetAdd("s", ["a", "b", "c", "d"]);

        IReadOnlyList<string> popped = Keyspace.SetPop("s", 3);

        popped.Should().HaveCount(3).And.OnlyHaveUniqueItems().And.BeSubsetOf(["a", "b", "c", "d"]);
        Keyspace.SetMembers("s").Should().ContainSingle().And.NotIntersectWith(popped);
    }

    [Fact]
    public void Pop_more_than_the_size_empties_and_deletes()
    {
        Keyspace.SetAdd("s", ["a", "b"]);

        Keyspace.SetPop("s", 10).Should().BeEquivalentTo(["a", "b"]);

        Keyspace.Exists("s").Should().BeFalse();
        Keyspace.SetPop("s", 1).Should().BeEmpty();
    }

    [Fact]
    public void Random_members_positive_count_are_distinct()
    {
        Keyspace.SetAdd("s", ["a", "b", "c"]);

        Keyspace.SetRandomMembers("s", 2).Should().HaveCount(2).And.OnlyHaveUniqueItems();
        Keyspace.SetRandomMembers("s", 10).Should().BeEquivalentTo(["a", "b", "c"]);
        Keyspace.SetCardinality("s").Should().Be(3);
    }

    [Fact]
    public void Random_members_negative_count_may_repeat()
    {
        Keyspace.SetAdd("s", ["only"]);

        Keyspace.SetRandomMembers("s", -5).Should().Equal("only", "only", "only", "only", "only");
    }

    [Fact]
    public void Move_transfers_a_member()
    {
        Keyspace.SetAdd("src", ["a", "b"]);
        Keyspace.SetAdd("dst", ["c"]);

        Keyspace.SetMove("src", "dst", "a").Should().BeTrue();
        Keyspace.SetMove("src", "dst", "zz").Should().BeFalse();

        Keyspace.SetMembers("src").Should().Equal("b");
        Keyspace.SetMembers("dst").Should().BeEquivalentTo(["a", "c"]);
    }

    [Fact]
    public void Move_creates_the_destination_and_deletes_an_emptied_source()
    {
        Keyspace.SetAdd("src", ["a"]);

        Keyspace.SetMove("src", "dst", "a").Should().BeTrue();

        Keyspace.Exists("src").Should().BeFalse();
        Keyspace.SetMembers("dst").Should().Equal("a");
    }

    [Fact]
    public void Move_to_the_same_set_is_a_no_op()
    {
        Keyspace.SetAdd("s", ["a"]);

        Keyspace.SetMove("s", "s", "a").Should().BeTrue();

        Keyspace.SetMembers("s").Should().Equal("a");
    }

    [Fact]
    public void Move_checks_the_destination_type()
    {
        Keyspace.SetAdd("src", ["a"]);
        Keyspace.SetString("dst", "text");

        Action act = () => Keyspace.SetMove("src", "dst", "a");

        act.Should().Throw<WrongTypeException>();
        Keyspace.SetMembers("src").Should().Equal("a");
    }

    [Fact]
    public void Combine_intersect_union_and_difference()
    {
        Keyspace.SetAdd("a", ["1", "2", "3"]);
        Keyspace.SetAdd("b", ["2", "3", "4"]);
        Keyspace.SetAdd("c", ["3", "5"]);

        Keyspace.SetCombine(SetOperation.Intersect, ["a", "b", "c"]).Should().BeEquivalentTo(["3"]);
        Keyspace.SetCombine(SetOperation.Union, ["a", "b", "c"]).Should().BeEquivalentTo(["1", "2", "3", "4", "5"]);
        Keyspace.SetCombine(SetOperation.Difference, ["a", "b", "c"]).Should().BeEquivalentTo(["1"]);
    }

    [Fact]
    public void Missing_keys_are_empty_sets()
    {
        Keyspace.SetAdd("a", ["1"]);

        Keyspace.SetCombine(SetOperation.Intersect, ["a", "missing"]).Should().BeEmpty();
        Keyspace.SetCombine(SetOperation.Union, ["a", "missing"]).Should().Equal("1");
        Keyspace.SetCombine(SetOperation.Difference, ["missing", "a"]).Should().BeEmpty();
    }

    [Fact]
    public void Combine_rejects_keys_of_other_types()
    {
        Keyspace.SetAdd("a", ["1"]);
        Keyspace.SetString("s", "x");

        Action act = () => Keyspace.SetCombine(SetOperation.Union, ["a", "s"]);

        act.Should().Throw<WrongTypeException>();
    }

    [Fact]
    public void Combine_and_store_replaces_the_destination()
    {
        Keyspace.SetAdd("a", ["1", "2"]);
        Keyspace.SetAdd("b", ["2", "3"]);
        Keyspace.SetString("dst", "old");
        Keyspace.Expire("dst", TimeSpan.FromSeconds(10));

        Keyspace.SetCombineAndStore(SetOperation.Union, "dst", ["a", "b"]).Should().Be(3);

        Keyspace.SetMembers("dst").Should().BeEquivalentTo(["1", "2", "3"]);
        Keyspace.GetTimeToLiveMilliseconds("dst").Should().Be(-1);
    }

    [Fact]
    public void Combine_and_store_can_use_the_destination_as_a_source()
    {
        Keyspace.SetAdd("a", ["1", "2"]);
        Keyspace.SetAdd("b", ["2"]);

        Keyspace.SetCombineAndStore(SetOperation.Difference, "a", ["a", "b"]).Should().Be(1);

        Keyspace.SetMembers("a").Should().Equal("1");
    }

    [Fact]
    public void Empty_result_deletes_the_destination()
    {
        Keyspace.SetAdd("a", ["1"]);
        Keyspace.SetAdd("dst", ["x"]);

        Keyspace.SetCombineAndStore(SetOperation.Intersect, "dst", ["a", "missing"]).Should().Be(0);

        Keyspace.Exists("dst").Should().BeFalse();
    }

    [Fact]
    public void Intersection_cardinality_with_and_without_limit()
    {
        Keyspace.SetAdd("a", ["1", "2", "3", "4"]);
        Keyspace.SetAdd("b", ["2", "3", "4", "5"]);

        Keyspace.SetIntersectionCardinality(["a", "b"], 0).Should().Be(3);
        Keyspace.SetIntersectionCardinality(["a", "b"], 2).Should().Be(2);
        Keyspace.SetIntersectionCardinality(["a", "missing"], 0).Should().Be(0);
    }
}
