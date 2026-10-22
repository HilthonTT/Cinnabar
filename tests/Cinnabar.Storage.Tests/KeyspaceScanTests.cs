namespace Cinnabar.Storage.Tests;

public class KeyspaceScanTests : KeyspaceTestBase
{
    private List<string> ScanAll(int count, string? pattern = null, KeyType? type = null)
    {
        var keys = new List<string>();
        ulong cursor = 0;
        int pages = 0;
        do
        {
            ScanPage<string> page = Keyspace.Scan(cursor, count, pattern, type);
            keys.AddRange(page.Items);
            cursor = page.Cursor;
            pages++;
            pages.Should().BeLessThan(10_000, "the iteration must terminate");
        }
        while (cursor != 0);

        return keys;
    }

    [Fact]
    public void Empty_keyspace_finishes_immediately()
    {
        ScanPage<string> page = Keyspace.Scan(0, 10);

        page.Cursor.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1000)]
    public void Full_iteration_returns_every_key_exactly_once(int count)
    {
        for (int i = 0; i < 250; i++)
        {
            Keyspace.SetString($"key:{i}", "v");
        }

        List<string> keys = ScanAll(count);

        keys.Should().HaveCount(250).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Count_bounds_the_page_size()
    {
        for (int i = 0; i < 100; i++)
        {
            Keyspace.SetString($"k{i}", "v");
        }

        ScanPage<string> page = Keyspace.Scan(0, 10);

        page.Items.Count.Should().BeInRange(10, 11);
        page.Cursor.Should().NotBe(0);
    }

    [Fact]
    public void Keys_present_throughout_survive_concurrent_changes()
    {
        for (int i = 0; i < 200; i++)
        {
            Keyspace.SetString($"stable:{i}", "v");
        }

        var seen = new List<string>();
        ulong cursor = 0;
        int round = 0;
        do
        {
            ScanPage<string> page = Keyspace.Scan(cursor, 15);
            seen.AddRange(page.Items);
            cursor = page.Cursor;

            // Churn between pages: add and remove other keys.
            Keyspace.SetString($"churn:{round}", "v");
            Keyspace.Delete($"churn:{round - 1}");
            round++;
        }
        while (cursor != 0);

        seen.Where(k => k.StartsWith("stable:")).Should().HaveCount(200).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Match_and_type_filter_results()
    {
        Keyspace.SetString("user:1", "v");
        Keyspace.SetString("user:2", "v");
        Keyspace.HashSetFields("user:3", Fields("f", "v"));
        Keyspace.SetString("order:1", "v");

        ScanAll(2, "user:*").Should().BeEquivalentTo(["user:1", "user:2", "user:3"]);
        ScanAll(2, type: KeyType.Hash).Should().Equal("user:3");
        ScanAll(2, "order:*", KeyType.Hash).Should().BeEmpty();
    }

    [Fact]
    public void Expired_keys_are_skipped()
    {
        Keyspace.SetString("live", "v");
        Keyspace.SetString("dying", "v", TimeSpan.FromSeconds(1));
        Time.Advance(TimeSpan.FromSeconds(2));

        ScanAll(10).Should().Equal("live");
    }

    [Fact]
    public void Hash_scan_returns_every_field()
    {
        var fields = Enumerable.Range(0, 50).SelectMany(i => new[] { $"f{i}", $"v{i}" }).ToArray();
        Keyspace.HashSetFields("h", Fields(fields));

        var seen = new List<KeyValuePair<string, string>>();
        ulong cursor = 0;
        do
        {
            ScanPage<KeyValuePair<string, string>> page = Keyspace.HashScan("h", cursor, 7);
            seen.AddRange(page.Items);
            cursor = page.Cursor;
        }
        while (cursor != 0);

        seen.Should().BeEquivalentTo(Fields(fields));
    }

    [Fact]
    public void Set_and_sorted_set_scans_filter_by_pattern()
    {
        Keyspace.SetAdd("s", ["apple", "avocado", "banana"]);
        Keyspace.SortedSetAdd("z", [new("apple", 1), new("banana", 2)]);

        Keyspace.SetScan("s", 0, 100, "a*").Items.Should().BeEquivalentTo(["apple", "avocado"]);
        Keyspace.SortedSetScan("z", 0, 100, "b*").Items.Should().Equal(new ScoredMember("banana", 2));

        ScanPage<string> missing = Keyspace.SetScan("missing", 0, 100);
        missing.Cursor.Should().Be(0);
        missing.Items.Should().BeEmpty();
    }

    [Fact]
    public void Collection_scan_on_wrong_type_throws()
    {
        Keyspace.SetString("str", "v");

        Action act = () => Keyspace.HashScan("str", 0, 10);

        act.Should().Throw<WrongTypeException>();
    }
}
