namespace Cinnabar.Storage.Tests;

public class KeyspaceMemoryTests : KeyspaceTestBase
{
    [Fact]
    public void Usage_starts_at_zero_and_returns_to_zero()
    {
        Keyspace.MemoryUsage.Should().Be(0);

        Keyspace.SetString("s", new string('x', 1000));
        Keyspace.ListPush("l", ["a", "b"], ListEnd.Right);
        Keyspace.HashSetFields("h", Fields("f", "v"));
        Keyspace.SetAdd("set", ["m"]);
        Keyspace.StreamAdd("x", "1-1", Fields("f", "v"));
        Keyspace.MemoryUsage.Should().BeGreaterThan(2000);

        foreach (string key in new[] { "s", "l", "h", "set", "x" })
        {
            Keyspace.Delete(key);
        }

        Keyspace.MemoryUsage.Should().Be(0);
    }

    [Fact]
    public void Usage_tracks_in_place_growth_and_shrinkage()
    {
        Keyspace.ListPush("l", ["item"], ListEnd.Right);
        long one = Keyspace.MemoryUsage;

        Keyspace.ListPush("l", Enumerable.Repeat("item", 99), ListEnd.Right);
        long hundred = Keyspace.MemoryUsage;
        Keyspace.ListTrim("l", 0, 0);

        // Per-element cost dominates; the per-key overhead is paid once.
        hundred.Should().BeGreaterThan(40 * one);
        Keyspace.MemoryUsage.Should().Be(one);
    }

    [Fact]
    public void Overwriting_replaces_the_old_size()
    {
        Keyspace.SetString("k", new string('x', 10_000));
        Keyspace.SetString("k", "small");
        long small = Keyspace.MemoryUsage;

        Keyspace.Delete("k");
        Keyspace.SetString("k", "small");

        Keyspace.MemoryUsage.Should().Be(small);
    }

    [Fact]
    public void Expired_keys_release_their_size_when_swept()
    {
        Keyspace.SetString("k", "v", expiry: TimeSpan.FromSeconds(1));
        Time.Advance(TimeSpan.FromSeconds(2));

        Keyspace.EvictExpired();

        Keyspace.MemoryUsage.Should().Be(0);
    }

    [Fact]
    public void NoEviction_evicts_nothing()
    {
        Keyspace.SetString("k", "v");

        Keyspace.Evict(0, EvictionPolicy.NoEviction).Should().BeEmpty();

        Keyspace.Exists("k").Should().BeTrue();
    }

    [Fact]
    public void Nothing_is_evicted_under_the_limit()
    {
        Keyspace.SetString("k", "v");

        Keyspace.Evict(long.MaxValue, EvictionPolicy.AllKeysLru).Should().BeEmpty();
    }

    [Fact]
    public void AllKeysLru_evicts_until_under_the_limit()
    {
        for (int i = 0; i < 100; i++)
        {
            Keyspace.SetString($"k{i}", new string('x', 100));
        }

        long limit = Keyspace.MemoryUsage / 2;
        IReadOnlyList<string> evicted = Keyspace.Evict(limit, EvictionPolicy.AllKeysLru);

        Keyspace.MemoryUsage.Should().BeLessThanOrEqualTo(limit);
        evicted.Should().OnlyHaveUniqueItems().And.HaveCountGreaterThan(40);
        evicted.Should().OnlyContain(key => !Keyspace.Exists(key));
        Keyspace.EvictedKeys.Should().Be(evicted.Count);
    }

    [Fact]
    public void AllKeysLru_prefers_keys_that_were_not_used_recently()
    {
        // With every key sampled, approximate LRU is exact.
        Keyspace.SetString("old", "v");
        Time.Advance(TimeSpan.FromSeconds(10));
        Keyspace.SetString("new", "v");
        Time.Advance(TimeSpan.FromSeconds(10));
        Keyspace.GetString("old");

        long oneKey = Keyspace.MemoryUsage / 2;
        Keyspace.Evict(oneKey, EvictionPolicy.AllKeysLru, samples: 50).Should().Equal("new");
    }

    [Fact]
    public void Volatile_policies_only_evict_keys_with_a_ttl()
    {
        Keyspace.SetString("forever", "v");
        Keyspace.SetString("temp", "v", expiry: TimeSpan.FromHours(1));

        Keyspace.Evict(0, EvictionPolicy.VolatileLru, samples: 50).Should().Equal("temp");

        Keyspace.Exists("forever").Should().BeTrue();
        Keyspace.Evict(0, EvictionPolicy.VolatileRandom, samples: 50).Should().BeEmpty();
    }

    [Fact]
    public void VolatileTtl_evicts_the_soonest_expiring_key()
    {
        Keyspace.SetString("late", "v", expiry: TimeSpan.FromHours(2));
        Keyspace.SetString("soon", "v", expiry: TimeSpan.FromMinutes(1));

        long oneKey = Keyspace.MemoryUsage / 2;
        Keyspace.Evict(oneKey, EvictionPolicy.VolatileTtl, samples: 50).Should().Equal("soon");
    }

    [Fact]
    public void AllKeysRandom_can_empty_the_keyspace()
    {
        for (int i = 0; i < 20; i++)
        {
            Keyspace.SetString($"k{i}", "v");
        }

        Keyspace.Evict(0, EvictionPolicy.AllKeysRandom).Should().HaveCount(20);

        Keyspace.Count.Should().Be(0);
    }

    [Fact]
    public void Snapshot_does_not_count_as_an_access()
    {
        Keyspace.SetString("a", "v");
        Time.Advance(TimeSpan.FromSeconds(10));
        Keyspace.SetString("b", "v");
        Time.Advance(TimeSpan.FromSeconds(10));

        Keyspace.Snapshot();

        long oneKey = Keyspace.MemoryUsage / 2;
        Keyspace.Evict(oneKey, EvictionPolicy.AllKeysLru, samples: 50).Should().Equal("a");
    }

    [Fact]
    public void Eviction_counts_as_a_change_for_watch()
    {
        Keyspace.SetString("k", "v");
        long version = Keyspace.Watch("k");

        Keyspace.Evict(0, EvictionPolicy.AllKeysLru);

        Keyspace.IsUnchanged("k", version).Should().BeFalse();
    }
}
