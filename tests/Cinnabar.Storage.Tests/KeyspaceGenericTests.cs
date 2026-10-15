namespace Cinnabar.Storage.Tests;

public class KeyspaceGenericTests : KeyspaceTestBase
{
    [Fact]
    public void Exists_is_false_for_missing_key()
    {
        Keyspace.Exists("missing").Should().BeFalse();
    }

    [Fact]
    public void Delete_existing_key_returns_true()
    {
        Keyspace.SetString("k", "v");

        Keyspace.Delete("k").Should().BeTrue();

        Keyspace.Exists("k").Should().BeFalse();
    }

    [Fact]
    public void Delete_missing_key_returns_false()
    {
        Keyspace.Delete("missing").Should().BeFalse();
    }

    [Fact]
    public void GetKeyType_reports_each_type()
    {
        Keyspace.SetString("s", "v");
        Keyspace.ListPush("l", ["a"], ListEnd.Left);
        Keyspace.StreamAdd("x", "1-1", Fields("f", "v"));

        Keyspace.GetKeyType("s").Should().Be(KeyType.String);
        Keyspace.GetKeyType("l").Should().Be(KeyType.List);
        Keyspace.GetKeyType("x").Should().Be(KeyType.Stream);
        Keyspace.GetKeyType("missing").Should().Be(KeyType.None);
    }

    [Fact]
    public void Keys_returns_all_keys_sorted_ordinally()
    {
        foreach (string key in new[] { "b", "a", "C", "aa" })
        {
            Keyspace.SetString(key, "v");
        }

        Keyspace.Keys().Should().Equal("C", "a", "aa", "b");
    }

    [Theory]
    [InlineData("user:*", new[] { "user:1", "user:10", "user:2" })]
    [InlineData("user:?", new[] { "user:1", "user:2" })]
    [InlineData("*:1*", new[] { "order:1", "user:1", "user:10" })]
    [InlineData("user:[12]", new[] { "user:1", "user:2" })]
    [InlineData("nomatch*", new string[0])]
    [InlineData("order:1", new[] { "order:1" })]
    public void Keys_filters_by_pattern(string pattern, string[] expected)
    {
        foreach (string key in new[] { "user:2", "user:1", "user:10", "order:1" })
        {
            Keyspace.SetString(key, "v");
        }

        Keyspace.Keys(pattern).Should().Equal(expected);
    }

    [Fact]
    public void Keys_includes_every_type()
    {
        Keyspace.SetString("s", "v");
        Keyspace.ListPush("l", ["a"], ListEnd.Left);
        Keyspace.StreamAdd("x", "1-1", Fields("f", "v"));

        Keyspace.Keys().Should().Equal("l", "s", "x");
    }

    [Fact]
    public void Ttl_of_missing_key_is_minus_two()
    {
        Keyspace.GetTimeToLiveMilliseconds("missing").Should().Be(-2);
    }

    [Fact]
    public void Ttl_of_key_without_expiry_is_minus_one()
    {
        Keyspace.SetString("k", "v");

        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(-1);
    }

    [Fact]
    public void Expire_sets_ttl()
    {
        Keyspace.SetString("k", "v");

        Keyspace.Expire("k", TimeSpan.FromSeconds(30)).Should().BeTrue();

        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(30_000);
    }

    [Fact]
    public void Expire_replaces_existing_ttl()
    {
        Keyspace.SetString("k", "v", expiry: TimeSpan.FromSeconds(30));

        Keyspace.Expire("k", TimeSpan.FromSeconds(5));

        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(5_000);
    }

    [Fact]
    public void Expire_missing_key_returns_false()
    {
        Keyspace.Expire("missing", TimeSpan.FromSeconds(1)).Should().BeFalse();

        Keyspace.Exists("missing").Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60_000)]
    public void Expire_with_non_positive_ttl_deletes_key(int milliseconds)
    {
        Keyspace.SetString("k", "v");

        Keyspace.Expire("k", TimeSpan.FromMilliseconds(milliseconds)).Should().BeTrue();

        Keyspace.Exists("k").Should().BeFalse();
        Keyspace.Count.Should().Be(0);
    }

    [Fact]
    public void Persist_removes_ttl()
    {
        Keyspace.SetString("k", "v", expiry: TimeSpan.FromSeconds(1));

        Keyspace.Persist("k").Should().BeTrue();
        Time.Advance(TimeSpan.FromHours(1));

        Keyspace.GetString("k").Should().Be("v");
        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(-1);
    }

    [Fact]
    public void Persist_key_without_ttl_returns_false()
    {
        Keyspace.SetString("k", "v");

        Keyspace.Persist("k").Should().BeFalse();
    }

    [Fact]
    public void Persist_missing_key_returns_false()
    {
        Keyspace.Persist("missing").Should().BeFalse();
    }

    [Fact]
    public void Expired_keys_are_invisible_everywhere()
    {
        Keyspace.SetString("s", "v", expiry: TimeSpan.FromSeconds(1));
        Keyspace.ListPush("l", ["a"], ListEnd.Left);
        Keyspace.Expire("l", TimeSpan.FromSeconds(1));
        Keyspace.StreamAdd("x", "1-1", Fields("f", "v"));
        Keyspace.Expire("x", TimeSpan.FromSeconds(1));

        Time.Advance(TimeSpan.FromSeconds(1));

        Keyspace.Keys().Should().BeEmpty();
        Keyspace.Snapshot().Should().BeEmpty();
        foreach (string key in new[] { "s", "l", "x" })
        {
            Keyspace.Exists(key).Should().BeFalse();
            Keyspace.GetKeyType(key).Should().Be(KeyType.None);
            Keyspace.GetTimeToLiveMilliseconds(key).Should().Be(-2);
            Keyspace.Persist(key).Should().BeFalse();
            Keyspace.Expire(key, TimeSpan.FromSeconds(10)).Should().BeFalse();
        }

        Keyspace.GetString("s").Should().BeNull();
        Keyspace.ListLength("l").Should().Be(0);
        Keyspace.ListRange("l", 0, -1).Should().BeEmpty();
        Keyspace.ListPop("l", ListEnd.Left).Should().BeEmpty();
        Keyspace.StreamLength("x").Should().Be(0);
        Keyspace.StreamLastId("x").Should().BeNull();
        Keyspace.StreamRange("x", StreamId.Min, StreamId.Max).Should().BeEmpty();
        Keyspace.StreamReadAfter("x", StreamId.Min).Should().BeEmpty();
    }

    [Fact]
    public void Delete_of_expired_key_returns_false()
    {
        Keyspace.SetString("k", "v", expiry: TimeSpan.FromSeconds(1));
        Time.Advance(TimeSpan.FromSeconds(1));

        Keyspace.Delete("k").Should().BeFalse();
    }

    [Fact]
    public void Expired_key_type_can_be_reused()
    {
        Keyspace.SetString("k", "v", expiry: TimeSpan.FromSeconds(1));
        Time.Advance(TimeSpan.FromSeconds(1));

        Keyspace.ListPush("k", ["a"], ListEnd.Left).Should().Be(1);

        Keyspace.GetKeyType("k").Should().Be(KeyType.List);
    }

    [Fact]
    public void Count_includes_expired_keys_until_evicted()
    {
        Keyspace.SetString("a", "v", expiry: TimeSpan.FromSeconds(1));
        Keyspace.SetString("b", "v", expiry: TimeSpan.FromSeconds(1));
        Keyspace.SetString("c", "v");
        Time.Advance(TimeSpan.FromSeconds(1));

        Keyspace.Count.Should().Be(3);

        Keyspace.EvictExpired().Should().Be(2);

        Keyspace.Count.Should().Be(1);
        Keyspace.Keys().Should().Equal("c");
    }

    [Fact]
    public void EvictExpired_with_nothing_expired_returns_zero()
    {
        Keyspace.SetString("a", "v", expiry: TimeSpan.FromSeconds(10));

        Keyspace.EvictExpired().Should().Be(0);
        Keyspace.Count.Should().Be(1);
    }

    [Fact]
    public void Background_sweep_evicts_on_timer()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using var keyspace = new Keyspace(time, TimeSpan.FromSeconds(1));
        keyspace.SetString("k", "v", expiry: TimeSpan.FromMilliseconds(500));

        time.Advance(TimeSpan.FromSeconds(1));

        keyspace.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_removes_everything()
    {
        Keyspace.SetString("s", "v");
        Keyspace.ListPush("l", ["a"], ListEnd.Left);
        Keyspace.StreamAdd("x", "1-1", Fields("f", "v"));

        Keyspace.Clear();

        Keyspace.Count.Should().Be(0);
        Keyspace.Keys().Should().BeEmpty();
    }

    [Fact]
    public void Count_counts_keys_of_all_types()
    {
        Keyspace.SetString("s", "v");
        Keyspace.ListPush("l", ["a"], ListEnd.Left);
        Keyspace.StreamAdd("x", "1-1", Fields("f", "v"));

        Keyspace.Count.Should().Be(3);
    }
}
