namespace Cinnabar.Storage.Tests;

public class KeyspaceStringTests : KeyspaceTestBase
{
    [Fact]
    public void Get_missing_key_returns_null()
    {
        Keyspace.GetString("missing").Should().BeNull();
    }

    [Fact]
    public void Set_then_get_returns_value()
    {
        Keyspace.SetString("k", "v").Should().BeTrue();

        Keyspace.GetString("k").Should().Be("v");
    }

    [Fact]
    public void Set_overwrites_existing_string()
    {
        Keyspace.SetString("k", "a");
        Keyspace.SetString("k", "b");

        Keyspace.GetString("k").Should().Be("b");
    }

    [Fact]
    public void Keys_are_case_sensitive()
    {
        Keyspace.SetString("Key", "upper");

        Keyspace.GetString("key").Should().BeNull();
    }

    [Fact]
    public void Set_if_not_exists_writes_missing_key()
    {
        Keyspace.SetString("k", "v", condition: SetCondition.IfNotExists).Should().BeTrue();

        Keyspace.GetString("k").Should().Be("v");
    }

    [Fact]
    public void Set_if_not_exists_does_not_overwrite()
    {
        Keyspace.SetString("k", "old");

        Keyspace.SetString("k", "new", condition: SetCondition.IfNotExists).Should().BeFalse();

        Keyspace.GetString("k").Should().Be("old");
    }

    [Fact]
    public void Set_if_not_exists_treats_expired_key_as_missing()
    {
        Keyspace.SetString("k", "old", expiry: TimeSpan.FromSeconds(1));
        Time.Advance(TimeSpan.FromSeconds(1));

        Keyspace.SetString("k", "new", condition: SetCondition.IfNotExists).Should().BeTrue();

        Keyspace.GetString("k").Should().Be("new");
    }

    [Fact]
    public void Set_if_exists_does_not_create_missing_key()
    {
        Keyspace.SetString("k", "v", condition: SetCondition.IfExists).Should().BeFalse();

        Keyspace.Exists("k").Should().BeFalse();
    }

    [Fact]
    public void Set_if_exists_overwrites_existing_key()
    {
        Keyspace.SetString("k", "old");

        Keyspace.SetString("k", "new", condition: SetCondition.IfExists).Should().BeTrue();

        Keyspace.GetString("k").Should().Be("new");
    }

    [Fact]
    public void Set_with_expiry_disappears_when_ttl_elapses()
    {
        Keyspace.SetString("k", "v", expiry: TimeSpan.FromSeconds(10));

        Time.Advance(TimeSpan.FromMilliseconds(9_999));
        Keyspace.GetString("k").Should().Be("v");

        Time.Advance(TimeSpan.FromMilliseconds(1));
        Keyspace.GetString("k").Should().BeNull();
    }

    [Fact]
    public void Set_with_expiry_reports_ttl()
    {
        Keyspace.SetString("k", "v", expiry: TimeSpan.FromSeconds(10));
        Time.Advance(TimeSpan.FromSeconds(3));

        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(7_000);
    }

    [Fact]
    public void Set_without_expiry_clears_previous_ttl()
    {
        Keyspace.SetString("k", "a", expiry: TimeSpan.FromSeconds(10));

        Keyspace.SetString("k", "b");

        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(-1);
    }

    [Fact]
    public void Set_with_keep_ttl_preserves_remaining_ttl()
    {
        Keyspace.SetString("k", "a", expiry: TimeSpan.FromSeconds(10));
        Time.Advance(TimeSpan.FromSeconds(2));

        Keyspace.SetString("k", "b", keepTtl: true);

        Keyspace.GetString("k").Should().Be("b");
        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(8_000);
    }

    [Fact]
    public void Set_with_keep_ttl_ignores_expiry_argument()
    {
        Keyspace.SetString("k", "a", expiry: TimeSpan.FromSeconds(10));

        Keyspace.SetString("k", "b", expiry: TimeSpan.FromSeconds(100), keepTtl: true);

        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(10_000);
    }

    [Fact]
    public void Set_with_keep_ttl_on_key_without_ttl_has_no_ttl()
    {
        Keyspace.SetString("k", "a");

        Keyspace.SetString("k", "b", keepTtl: true);

        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(-1);
    }

    [Fact]
    public void Set_with_keep_ttl_on_missing_key_has_no_ttl()
    {
        Keyspace.SetString("k", "b", keepTtl: true);

        Keyspace.GetTimeToLiveMilliseconds("k").Should().Be(-1);
    }

    [Fact]
    public void Set_replaces_a_list()
    {
        Keyspace.ListPush("k", ["a"], ListEnd.Right);

        Keyspace.SetString("k", "v").Should().BeTrue();

        Keyspace.GetKeyType("k").Should().Be(KeyType.String);
        Keyspace.GetString("k").Should().Be("v");
    }

    [Fact]
    public void Set_replaces_a_stream()
    {
        Keyspace.StreamAdd("k", "1-1", Fields("f", "v"));

        Keyspace.SetString("k", "v").Should().BeTrue();

        Keyspace.GetKeyType("k").Should().Be(KeyType.String);
        Keyspace.GetString("k").Should().Be("v");
    }

    [Fact]
    public void Increment_missing_key_starts_from_zero()
    {
        Keyspace.IncrementBy("n", 5).Should().Be(5);

        Keyspace.GetString("n").Should().Be("5");
    }

    [Fact]
    public void Increment_existing_integer()
    {
        Keyspace.SetString("n", "10");

        Keyspace.IncrementBy("n", -3).Should().Be(7);

        Keyspace.GetString("n").Should().Be("7");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("12abc")]
    [InlineData("99999999999999999999")]
    public void Increment_non_integer_fails(string value)
    {
        Keyspace.SetString("n", value);

        Action act = () => Keyspace.IncrementBy("n", 1);

        act.Should().Throw<StorageException>().WithMessage("ERR value is not an integer or out of range");
        Keyspace.GetString("n").Should().Be(value);
    }

    [Theory]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(long.MinValue, -1L)]
    [InlineData(1L, long.MaxValue)]
    public void Increment_overflow_fails(long start, long delta)
    {
        Keyspace.SetString("n", start.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Action act = () => Keyspace.IncrementBy("n", delta);

        act.Should().Throw<StorageException>().WithMessage("ERR increment or decrement would overflow");
    }

    [Fact]
    public void Increment_failure_is_not_a_wrong_type_error()
    {
        Keyspace.SetString("n", "abc");

        Action act = () => Keyspace.IncrementBy("n", 1);

        act.Should().Throw<StorageException>().Which.Should().NotBeOfType<WrongTypeException>();
    }

    [Fact]
    public void Increment_keeps_ttl()
    {
        Keyspace.SetString("n", "1", expiry: TimeSpan.FromSeconds(10));
        Time.Advance(TimeSpan.FromSeconds(4));

        Keyspace.IncrementBy("n", 1);

        Keyspace.GetTimeToLiveMilliseconds("n").Should().Be(6_000);
    }

    [Fact]
    public void Increment_expired_key_starts_from_zero_without_ttl()
    {
        Keyspace.SetString("n", "100", expiry: TimeSpan.FromSeconds(1));
        Time.Advance(TimeSpan.FromSeconds(2));

        Keyspace.IncrementBy("n", 1).Should().Be(1);

        Keyspace.GetTimeToLiveMilliseconds("n").Should().Be(-1);
    }
}
