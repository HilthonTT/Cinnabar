namespace Cinnabar.Storage.Tests;

/// <summary>MGET, MSET and the atomicity of operations that span several keys.</summary>
public class KeyspaceMultiKeyTests : KeyspaceTestBase
{
    private static readonly ParallelOptions Parallelism = new() { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount) };

    [Fact]
    public void Get_strings_returns_null_for_missing_and_other_types()
    {
        Keyspace.SetString("a", "1");
        Keyspace.ListPush("list", ["x"], ListEnd.Left);

        Keyspace.GetStrings(["a", "missing", "list", "a"]).Should().Equal("1", null, null, "1");
    }

    [Fact]
    public void Set_strings_replaces_any_type_and_clears_ttl()
    {
        Keyspace.ListPush("list", ["x"], ListEnd.Left);
        Keyspace.SetString("ttl", "old", TimeSpan.FromSeconds(10));

        Keyspace.SetStrings([new("list", "1"), new("ttl", "2"), new("list", "3")]).Should().BeTrue();

        Keyspace.GetString("list").Should().Be("3");
        Keyspace.GetString("ttl").Should().Be("2");
        Keyspace.GetTimeToLiveMilliseconds("ttl").Should().Be(-1);
    }

    [Fact]
    public void Set_strings_if_none_exist_is_all_or_nothing()
    {
        Keyspace.SetString("b", "old");

        Keyspace.SetStrings([new("a", "1"), new("b", "2")], onlyIfNoneExist: true).Should().BeFalse();
        Keyspace.Exists("a").Should().BeFalse();
        Keyspace.GetString("b").Should().Be("old");

        Keyspace.SetStrings([new("a", "1"), new("c", "3")], onlyIfNoneExist: true).Should().BeTrue();
        Keyspace.GetStrings(["a", "c"]).Should().Equal("1", "3");
    }

    [Fact]
    public void String_length_counts_chars_which_are_bytes()
    {
        Keyspace.SetString("k", ByteString.FromText("héllo"));

        Keyspace.StringLength("k").Should().Be(6);
        Keyspace.StringLength("missing").Should().Be(0);
    }

    [Fact]
    public void Mset_is_never_seen_half_done()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);
        string[] keys = [.. Enumerable.Range(0, 16).Select(i => $"k{i}")];
        keyspace.SetStrings([.. keys.Select(k => new KeyValuePair<string, string>(k, "0"))]);
        int torn = 0;

        Parallel.Invoke(
            Parallelism,
            () =>
            {
                for (int round = 1; round <= 2_000; round++)
                {
                    string value = round.ToString();
                    keyspace.SetStrings([.. keys.Select(k => new KeyValuePair<string, string>(k, value))]);
                }
            },
            () =>
            {
                for (int i = 0; i < 2_000; i++)
                {
                    if (keyspace.GetStrings(keys).Distinct().Count() != 1)
                    {
                        Interlocked.Increment(ref torn);
                    }
                }
            });

        torn.Should().Be(0);
    }

    [Fact]
    public void Concurrent_smoves_conserve_members_and_do_not_deadlock()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);
        keyspace.SetAdd("a", Enumerable.Range(0, 500).Select(i => $"m{i}"));

        // Opposite directions take the same two stripes in opposite key order.
        Parallel.For(0, 20_000, Parallelism, i =>
        {
            string member = $"m{i % 500}";
            if (i % 2 == 0)
            {
                keyspace.SetMove("a", "b", member);
            }
            else
            {
                keyspace.SetMove("b", "a", member);
            }
        });

        (keyspace.SetCardinality("a") + keyspace.SetCardinality("b")).Should().Be(500);
        keyspace.SetCombine(SetOperation.Union, ["a", "b"]).Should().HaveCount(500);
    }
}
