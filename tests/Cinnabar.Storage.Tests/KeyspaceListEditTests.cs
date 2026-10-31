namespace Cinnabar.Storage.Tests;

public class KeyspaceListEditTests : KeyspaceTestBase
{
    private void List(string key, params string[] values) => Keyspace.ListPush(key, values, ListEnd.Right);

    private IReadOnlyList<string> All(string key) => Keyspace.ListRange(key, 0, -1);

    [Fact]
    public void PushIfExists_only_pushes_onto_existing_lists()
    {
        Keyspace.ListPushIfExists("missing", ["a"], ListEnd.Left).Should().Be(0);
        Keyspace.Exists("missing").Should().BeFalse();

        List("l", "b");
        Keyspace.ListPushIfExists("l", ["a"], ListEnd.Left).Should().Be(2);
        Keyspace.ListPushIfExists("l", ["c", "d"], ListEnd.Right).Should().Be(4);
        All("l").Should().Equal("a", "b", "c", "d");
    }

    [Theory]
    [InlineData(0, "a")]
    [InlineData(2, "c")]
    [InlineData(-1, "c")]
    [InlineData(-3, "a")]
    [InlineData(3, null)]
    [InlineData(-4, null)]
    public void Index_supports_negative_indexes(long index, string? expected)
    {
        List("l", "a", "b", "c");

        Keyspace.ListIndex("l", index).Should().Be(expected);
    }

    [Fact]
    public void Index_of_missing_key_is_null() => Keyspace.ListIndex("missing", 0).Should().BeNull();

    [Fact]
    public void Set_replaces_an_element()
    {
        List("l", "a", "b", "c");

        Keyspace.ListSet("l", -2, "B");

        All("l").Should().Equal("a", "B", "c");
    }

    [Fact]
    public void Set_errors_on_missing_key_and_bad_index()
    {
        List("l", "a");

        FluentActions.Invoking(() => Keyspace.ListSet("missing", 0, "x"))
            .Should().Throw<StorageException>().WithMessage("ERR no such key");
        FluentActions.Invoking(() => Keyspace.ListSet("l", 5, "x"))
            .Should().Throw<StorageException>().WithMessage("ERR index out of range");
    }

    [Fact]
    public void Insert_goes_before_or_after_the_first_pivot()
    {
        List("l", "a", "x", "x");

        Keyspace.ListInsert("l", before: true, "x", "1").Should().Be(4);
        Keyspace.ListInsert("l", before: false, "x", "2").Should().Be(5);

        All("l").Should().Equal("a", "1", "x", "2", "x");
    }

    [Fact]
    public void Insert_reports_missing_pivot_and_missing_key()
    {
        List("l", "a");

        Keyspace.ListInsert("l", before: true, "zzz", "v").Should().Be(-1);
        Keyspace.ListInsert("missing", before: true, "a", "v").Should().Be(0);
        Keyspace.Exists("missing").Should().BeFalse();
    }

    [Fact]
    public void Remove_positive_count_removes_from_head()
    {
        List("l", "x", "a", "x", "b", "x");

        Keyspace.ListRemove("l", 2, "x").Should().Be(2);

        All("l").Should().Equal("a", "b", "x");
    }

    [Fact]
    public void Remove_negative_count_removes_from_tail()
    {
        List("l", "x", "a", "x", "b", "x");

        Keyspace.ListRemove("l", -2, "x").Should().Be(2);

        All("l").Should().Equal("x", "a", "b");
    }

    [Fact]
    public void Remove_zero_removes_all_and_deletes_empty_list()
    {
        List("l", "x", "x");

        Keyspace.ListRemove("l", 0, "x").Should().Be(2);

        Keyspace.Exists("l").Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 2, new[] { "b", "c" })]
    [InlineData(0, -1, new[] { "a", "b", "c", "d" })]
    [InlineData(-2, -1, new[] { "c", "d" })]
    [InlineData(1, 100, new[] { "b", "c", "d" })]
    public void Trim_keeps_the_range(long start, long stop, string[] expected)
    {
        List("l", "a", "b", "c", "d");

        Keyspace.ListTrim("l", start, stop);

        All("l").Should().Equal(expected);
    }

    [Fact]
    public void Trim_to_an_empty_range_deletes_the_list()
    {
        List("l", "a", "b");

        Keyspace.ListTrim("l", 5, 10).Should().BeTrue();

        Keyspace.Exists("l").Should().BeFalse();
    }

    [Fact]
    public void Trim_that_keeps_everything_reports_no_change()
    {
        List("l", "a", "b");
        long version = Keyspace.Watch("l");

        Keyspace.ListTrim("l", 0, -1).Should().BeFalse();

        Keyspace.IsUnchanged("l", version).Should().BeTrue();
    }

    [Fact]
    public void Positions_honour_rank_count_and_maxlen()
    {
        List("l", "a", "x", "b", "x", "c", "x");

        Keyspace.ListPositions("l", "x", rank: 1, count: 1, maxLength: 0).Should().Equal(1);
        Keyspace.ListPositions("l", "x", rank: 2, count: 0, maxLength: 0).Should().Equal(3, 5);
        Keyspace.ListPositions("l", "x", rank: -1, count: 2, maxLength: 0).Should().Equal(5, 3);
        Keyspace.ListPositions("l", "x", rank: 1, count: 0, maxLength: 3).Should().Equal(1);
        Keyspace.ListPositions("l", "nope", rank: 1, count: 0, maxLength: 0).Should().BeEmpty();
    }

    [Fact]
    public void Move_pops_and_pushes_atomically()
    {
        List("src", "a", "b", "c");
        List("dst", "x");

        Keyspace.ListMove("src", "dst", ListEnd.Right, ListEnd.Left).Should().Be("c");
        Keyspace.ListMove("src", "dst", ListEnd.Left, ListEnd.Right).Should().Be("a");

        All("src").Should().Equal("b");
        All("dst").Should().Equal("c", "x", "a");
    }

    [Fact]
    public void Move_creates_destination_and_deletes_empty_source()
    {
        List("src", "a");

        Keyspace.ListMove("src", "dst", ListEnd.Left, ListEnd.Left).Should().Be("a");

        Keyspace.Exists("src").Should().BeFalse();
        All("dst").Should().Equal("a");
    }

    [Fact]
    public void Move_onto_the_same_list_rotates_it()
    {
        List("l", "a", "b", "c");

        Keyspace.ListMove("l", "l", ListEnd.Right, ListEnd.Left).Should().Be("c");

        All("l").Should().Equal("c", "a", "b");
    }

    [Fact]
    public void Move_from_missing_source_returns_null()
    {
        Keyspace.ListMove("missing", "dst", ListEnd.Left, ListEnd.Left).Should().BeNull();
        Keyspace.Exists("dst").Should().BeFalse();
    }

    [Fact]
    public void Move_to_wrong_type_changes_nothing()
    {
        List("src", "a");
        Keyspace.SetString("dst", "text");

        FluentActions.Invoking(() => Keyspace.ListMove("src", "dst", ListEnd.Left, ListEnd.Left))
            .Should().Throw<WrongTypeException>();

        All("src").Should().Equal("a");
    }
}
