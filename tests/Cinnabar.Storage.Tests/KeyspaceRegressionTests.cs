namespace Cinnabar.Storage.Tests;

/// <summary>Regression tests for bugs found in an audit of the storage layer.</summary>
public class KeyspaceRegressionTests : KeyspaceTestBase
{
    [Fact]
    public void Snapshot_survives_an_expiry_beyond_year_9999()
    {
        Keyspace.SetString("k", "v");
        Keyspace.ExpireAt("k", long.MaxValue);

        KeySnapshot snapshot = Keyspace.Snapshot().Should().ContainSingle().Subject;

        snapshot.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()));
    }

    [Theory]
    [InlineData("007")]
    [InlineData("+5")]
    [InlineData("-0")]
    [InlineData(" 5")]
    [InlineData("")]
    public void Increment_rejects_integers_redis_rejects(string stored)
    {
        Keyspace.SetString("k", stored);

        Action act = () => Keyspace.IncrementBy("k", 1);

        act.Should().Throw<StorageException>().WithMessage("ERR value is not an integer or out of range");
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-5", -4)]
    [InlineData("9223372036854775806", 9223372036854775807)]
    public void Increment_accepts_canonical_integers(string stored, long expected)
    {
        Keyspace.SetString("k", stored);

        Keyspace.IncrementBy("k", 1).Should().Be(expected);
    }

    [Fact]
    public void Hash_increment_rejects_leading_zeros()
    {
        Keyspace.HashSetFields("h", [new("f", "007")]);

        Action act = () => Keyspace.HashIncrementBy("h", "f", 1);

        act.Should().Throw<StorageException>().WithMessage("ERR hash value is not an integer");
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("1e-400")]
    public void Double_parse_rejects_overflow_and_underflow(string text)
    {
        DoubleText.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("inf")]
    [InlineData("-inf")]
    [InlineData("0")]
    [InlineData("0e10")]
    [InlineData("-0.0")]
    [InlineData("4.9e-324")]
    [InlineData("1.7976931348623157e308")]
    public void Double_parse_accepts_values_in_range(string text)
    {
        DoubleText.TryParse(text, out _).Should().BeTrue();
    }

    [Fact]
    public void Volatile_eviction_finds_the_few_keys_with_a_ttl()
    {
        for (int i = 0; i < 5000; i++)
        {
            Keyspace.SetString($"plain:{i}", "v");
        }

        Keyspace.SetString("volatile", "v");
        Keyspace.ExpireAt("volatile", long.MaxValue);

        IReadOnlyList<string> evicted = Keyspace.Evict(Keyspace.MemoryUsage - 1, EvictionPolicy.VolatileLru);

        evicted.Should().Equal("volatile");
    }

    [Fact]
    public void Volatile_eviction_ignores_keys_whose_ttl_was_removed()
    {
        Keyspace.SetString("k", "v");
        Keyspace.ExpireAt("k", long.MaxValue);
        Keyspace.Persist("k");

        Keyspace.Evict(0, EvictionPolicy.VolatileRandom).Should().BeEmpty();
        Keyspace.Exists("k").Should().BeTrue();
    }

    [Fact]
    public void Smove_from_missing_source_returns_false_even_if_destination_has_another_type()
    {
        Keyspace.SetString("d", "x");

        Keyspace.SetMove("nosuch", "d", "m").Should().BeFalse();
    }

    [Fact]
    public void Smove_with_wrong_type_destination_fails_even_if_member_is_absent()
    {
        Keyspace.SetAdd("s", ["a"]);
        Keyspace.SetString("d", "x");

        Action act = () => Keyspace.SetMove("s", "d", "absent");

        act.Should().Throw<WrongTypeException>();
    }

    [Fact]
    public void Xadd_rejects_a_sequence_before_the_star()
    {
        Action act = () => Keyspace.StreamAdd("s", "5-3-*", [new("f", "v")]);

        act.Should().Throw<StorageException>();
        Keyspace.Exists("s").Should().BeFalse();
    }

    [Fact]
    public void Stream_memory_grows_with_pending_entries()
    {
        for (int i = 1; i <= 100; i++)
        {
            Keyspace.StreamAdd("s", $"{i}-0", [new("f", "v")]);
        }

        Keyspace.StreamGroupCreate("s", "g", "0");
        long before = Keyspace.MemoryUsage;

        Keyspace.StreamReadGroup("s", "g", "c", after: null);

        Keyspace.MemoryUsage.Should().BeGreaterThan(before + (100 * 50));
    }
}
