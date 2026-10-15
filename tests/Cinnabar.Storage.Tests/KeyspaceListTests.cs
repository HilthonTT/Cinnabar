namespace Cinnabar.Storage.Tests;

public class KeyspaceListTests : KeyspaceTestBase
{
    [Fact]
    public void Push_left_inserts_each_value_at_head()
    {
        Keyspace.ListPush("l", ["a", "b", "c"], ListEnd.Left).Should().Be(3);

        Keyspace.ListRange("l", 0, -1).Should().Equal("c", "b", "a");
    }

    [Fact]
    public void Push_right_appends_in_order()
    {
        Keyspace.ListPush("l", ["a", "b", "c"], ListEnd.Right).Should().Be(3);

        Keyspace.ListRange("l", 0, -1).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Push_returns_length_after_push()
    {
        Keyspace.ListPush("l", ["a"], ListEnd.Right);

        Keyspace.ListPush("l", ["b", "c"], ListEnd.Left).Should().Be(3);

        Keyspace.ListRange("l", 0, -1).Should().Equal("c", "b", "a");
    }

    [Fact]
    public void Push_creates_key_of_type_list()
    {
        Keyspace.ListPush("l", ["a"], ListEnd.Right);

        Keyspace.GetKeyType("l").Should().Be(KeyType.List);
    }

    [Fact]
    public void Push_of_no_values_does_not_create_an_empty_list()
    {
        // Redis never keeps empty lists; a list key always has at least one element.
        Keyspace.ListPush("l", [], ListEnd.Right).Should().Be(0);

        Keyspace.Exists("l").Should().BeFalse();
    }

    [Fact]
    public void Pop_left_removes_from_head()
    {
        Keyspace.ListPush("l", ["a", "b", "c"], ListEnd.Right);

        Keyspace.ListPop("l", ListEnd.Left).Should().Equal("a");
        Keyspace.ListRange("l", 0, -1).Should().Equal("b", "c");
    }

    [Fact]
    public void Pop_right_removes_from_tail()
    {
        Keyspace.ListPush("l", ["a", "b", "c"], ListEnd.Right);

        Keyspace.ListPop("l", ListEnd.Right).Should().Equal("c");
        Keyspace.ListRange("l", 0, -1).Should().Equal("a", "b");
    }

    [Fact]
    public void Pop_count_returns_in_removal_order()
    {
        Keyspace.ListPush("l", ["a", "b", "c", "d"], ListEnd.Right);

        Keyspace.ListPop("l", ListEnd.Right, 2).Should().Equal("d", "c");
        Keyspace.ListRange("l", 0, -1).Should().Equal("a", "b");
    }

    [Fact]
    public void Pop_count_larger_than_list_returns_everything()
    {
        Keyspace.ListPush("l", ["a", "b"], ListEnd.Right);

        Keyspace.ListPop("l", ListEnd.Left, 10).Should().Equal("a", "b");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Pop_non_positive_count_returns_nothing(int count)
    {
        Keyspace.ListPush("l", ["a"], ListEnd.Right);

        Keyspace.ListPop("l", ListEnd.Left, count).Should().BeEmpty();
        Keyspace.ListLength("l").Should().Be(1);
    }

    [Fact]
    public void Pop_missing_key_returns_empty()
    {
        Keyspace.ListPop("missing", ListEnd.Left).Should().BeEmpty();
    }

    [Fact]
    public void Popping_last_element_deletes_key()
    {
        Keyspace.ListPush("l", ["a"], ListEnd.Right);

        Keyspace.ListPop("l", ListEnd.Left);

        Keyspace.Exists("l").Should().BeFalse();
        Keyspace.GetKeyType("l").Should().Be(KeyType.None);
        Keyspace.Count.Should().Be(0);
    }

    [Fact]
    public void Length_of_missing_key_is_zero()
    {
        Keyspace.ListLength("missing").Should().Be(0);
    }

    [Fact]
    public void Length_counts_elements()
    {
        Keyspace.ListPush("l", ["a", "b", "c"], ListEnd.Right);

        Keyspace.ListLength("l").Should().Be(3);
    }

    [Theory]
    [InlineData(0, -1, new[] { "a", "b", "c", "d", "e" })]
    [InlineData(0, 0, new[] { "a" })]
    [InlineData(1, 3, new[] { "b", "c", "d" })]
    [InlineData(-2, -1, new[] { "d", "e" })]
    [InlineData(-3, 3, new[] { "c", "d" })]
    [InlineData(-100, 1, new[] { "a", "b" })]
    [InlineData(3, 100, new[] { "d", "e" })]
    [InlineData(-100, 100, new[] { "a", "b", "c", "d", "e" })]
    [InlineData(5, 10, new string[0])]
    [InlineData(3, 1, new string[0])]
    [InlineData(-1, -2, new string[0])]
    [InlineData(0, -6, new string[0])]
    [InlineData(-100, -100, new string[0])]
    [InlineData(long.MinValue, long.MaxValue, new[] { "a", "b", "c", "d", "e" })]
    public void Range_follows_redis_lrange_rules(long start, long stop, string[] expected)
    {
        Keyspace.ListPush("l", ["a", "b", "c", "d", "e"], ListEnd.Right);

        Keyspace.ListRange("l", start, stop).Should().Equal(expected);
    }

    [Fact]
    public void Range_of_missing_key_is_empty()
    {
        Keyspace.ListRange("missing", 0, -1).Should().BeEmpty();
    }

    [Fact]
    public void Push_to_expired_list_starts_new_list_without_ttl()
    {
        Keyspace.ListPush("l", ["old"], ListEnd.Right);
        Keyspace.Expire("l", TimeSpan.FromSeconds(1));
        Time.Advance(TimeSpan.FromSeconds(1));

        Keyspace.ListPush("l", ["new"], ListEnd.Right).Should().Be(1);

        Keyspace.ListRange("l", 0, -1).Should().Equal("new");
        Keyspace.GetTimeToLiveMilliseconds("l").Should().Be(-1);
    }

    [Fact]
    public void Push_keeps_ttl_of_existing_list()
    {
        Keyspace.ListPush("l", ["a"], ListEnd.Right);
        Keyspace.Expire("l", TimeSpan.FromSeconds(10));

        Keyspace.ListPush("l", ["b"], ListEnd.Right);

        Keyspace.GetTimeToLiveMilliseconds("l").Should().Be(10_000);
    }
}
