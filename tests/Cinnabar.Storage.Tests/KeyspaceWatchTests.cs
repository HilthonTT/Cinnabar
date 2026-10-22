namespace Cinnabar.Storage.Tests;

public class KeyspaceWatchTests : KeyspaceTestBase
{
    private bool ChangedAfter(string key, Action change)
    {
        long version = Keyspace.Watch(key);
        change();
        bool unchanged = Keyspace.IsUnchanged(key, version);
        Keyspace.Unwatch(key);
        return !unchanged;
    }

    [Fact]
    public void Untouched_key_is_unchanged()
    {
        Keyspace.SetString("k", "v");

        ChangedAfter("k", () => Keyspace.GetString("k")).Should().BeFalse();
    }

    [Fact]
    public void Writes_to_other_keys_do_not_count()
    {
        Keyspace.SetString("k", "v");

        ChangedAfter("k", () => Keyspace.SetString("other", "v")).Should().BeFalse();
    }

    public static TheoryData<string> Changes => new()
    {
        "set", "increment", "push", "pop", "stream", "hash", "set-add", "zadd", "expire", "persist", "delete", "mset", "smove", "store",
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Every_kind_of_write_counts(string change)
    {
        string key = change switch
        {
            "set" or "increment" or "expire" or "persist" or "delete" or "mset" => "string",
            "push" or "pop" => "list",
            "stream" => "stream",
            "hash" => "hash",
            "set-add" or "smove" or "store" => "set",
            _ => "zset",
        };

        Keyspace.SetString("string", "1", TimeSpan.FromHours(1));
        Keyspace.ListPush("list", ["a", "b"], ListEnd.Right);
        Keyspace.StreamAdd("stream", "1-1", Fields("f", "v"));
        Keyspace.HashSetFields("hash", Fields("f", "v"));
        Keyspace.SetAdd("set", ["a"]);
        Keyspace.SetAdd("other-set", ["b"]);
        Keyspace.SortedSetAdd("zset", [new("a", 1)]);

        Action action = change switch
        {
            "set" => () => Keyspace.SetString("string", "2"),
            "increment" => () => Keyspace.IncrementBy("string", 1),
            "push" => () => Keyspace.ListPush("list", ["c"], ListEnd.Left),
            "pop" => () => Keyspace.ListPop("list", ListEnd.Left),
            "stream" => () => Keyspace.StreamAdd("stream", "*", Fields("f", "v")),
            "hash" => () => Keyspace.HashSetFields("hash", Fields("g", "w")),
            "set-add" => () => Keyspace.SetAdd("set", ["new"]),
            "zadd" => () => Keyspace.SortedSetAdd("zset", [new("a", 2)]),
            "expire" => () => Keyspace.Expire("string", TimeSpan.FromHours(2)),
            "persist" => () => Keyspace.Persist("string"),
            "delete" => () => Keyspace.Delete("string"),
            "mset" => () => Keyspace.SetStrings([new("string", "x")]),
            "smove" => () => Keyspace.SetMove("other-set", "set", "b"),
            "store" => () => Keyspace.SetCombineAndStore(SetOperation.Union, "set", ["other-set"]),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        ChangedAfter(key, action).Should().BeTrue();
    }

    [Fact]
    public void No_op_writes_do_not_count()
    {
        Keyspace.SetAdd("set", ["a"]);
        Keyspace.HashSetFields("hash", Fields("f", "v"));

        ChangedAfter("set", () => Keyspace.SetAdd("set", ["a"])).Should().BeFalse();
        ChangedAfter("set", () => Keyspace.SetRemove("set", ["zz"])).Should().BeFalse();
        ChangedAfter("hash", () => Keyspace.HashDelete("hash", ["zz"])).Should().BeFalse();
    }

    [Fact]
    public void Delete_and_recreate_counts()
    {
        Keyspace.SetString("k", "v");

        ChangedAfter("k", () =>
        {
            Keyspace.Delete("k");
            Keyspace.SetString("k", "v");
        }).Should().BeTrue();
    }

    [Fact]
    public void Create_and_delete_of_a_missing_key_counts()
    {
        ChangedAfter("k", () =>
        {
            Keyspace.SetString("k", "v");
            Keyspace.Delete("k");
        }).Should().BeTrue();
    }

    [Fact]
    public void Missing_key_that_stays_missing_is_unchanged()
    {
        ChangedAfter("k", () => Keyspace.Delete("k")).Should().BeFalse();
    }

    [Fact]
    public void Expiry_counts_as_a_change()
    {
        Keyspace.SetString("k", "v", TimeSpan.FromSeconds(1));

        ChangedAfter("k", () => Time.Advance(TimeSpan.FromSeconds(2))).Should().BeTrue();
    }

    [Fact]
    public void Flush_counts_as_a_change()
    {
        Keyspace.SetString("k", "v");

        ChangedAfter("k", Keyspace.Clear).Should().BeTrue();
    }

    [Fact]
    public void Unwatch_releases_bookkeeping()
    {
        Keyspace.Watch("a");
        Keyspace.Watch("a");
        Keyspace.Watch("b");
        Keyspace.Delete("a");

        Keyspace.WatchedKeyCount.Should().Be(2);
        Keyspace.Unwatch("a");
        Keyspace.Unwatch("b");
        Keyspace.WatchedKeyCount.Should().Be(1);
        Keyspace.Unwatch("a");
        Keyspace.WatchedKeyCount.Should().Be(0);
        Keyspace.Unwatch("never-watched");
    }

    [Fact]
    public void Two_watchers_see_the_same_change()
    {
        Keyspace.SetString("k", "v");
        long first = Keyspace.Watch("k");
        long second = Keyspace.Watch("k");

        Keyspace.SetString("k", "w");

        Keyspace.IsUnchanged("k", first).Should().BeFalse();
        Keyspace.IsUnchanged("k", second).Should().BeFalse();
    }
}
