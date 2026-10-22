namespace Cinnabar.Storage.Tests;

public class KeyspaceHashTests : KeyspaceTestBase
{
    [Fact]
    public void Set_counts_only_new_fields()
    {
        Keyspace.HashSetFields("h", Fields("a", "1", "b", "2")).Should().Be(2);
        Keyspace.HashSetFields("h", Fields("b", "3", "c", "4")).Should().Be(1);

        Keyspace.HashGetAll("h").Should().BeEquivalentTo(Fields("a", "1", "b", "3", "c", "4"));
        Keyspace.GetKeyType("h").Should().Be(KeyType.Hash);
    }

    [Fact]
    public void Repeated_field_in_one_call_keeps_the_last_value()
    {
        Keyspace.HashSetFields("h", Fields("f", "1", "f", "2")).Should().Be(1);

        Keyspace.HashGet("h", "f").Should().Be("2");
    }

    [Fact]
    public void Get_and_get_many_return_null_for_missing()
    {
        Keyspace.HashSetFields("h", Fields("a", "1"));

        Keyspace.HashGet("h", "zz").Should().BeNull();
        Keyspace.HashGet("missing", "a").Should().BeNull();
        Keyspace.HashGetMany("h", ["a", "zz"]).Should().Equal("1", null);
    }

    [Fact]
    public void Set_if_not_exists_only_writes_new_fields()
    {
        Keyspace.HashSetIfNotExists("h", "f", "1").Should().BeTrue();
        Keyspace.HashSetIfNotExists("h", "f", "2").Should().BeFalse();

        Keyspace.HashGet("h", "f").Should().Be("1");
    }

    [Fact]
    public void Deleting_the_last_field_deletes_the_key()
    {
        Keyspace.HashSetFields("h", Fields("a", "1", "b", "2"));

        Keyspace.HashDelete("h", ["a", "zz"]).Should().Be(1);
        Keyspace.Exists("h").Should().BeTrue();
        Keyspace.HashDelete("h", ["b"]).Should().Be(1);

        Keyspace.Exists("h").Should().BeFalse();
        Keyspace.HashDelete("h", ["b"]).Should().Be(0);
    }

    [Fact]
    public void Exists_length_and_string_length()
    {
        Keyspace.HashSetFields("h", Fields("a", "hello", "b", ""));

        Keyspace.HashExists("h", "a").Should().BeTrue();
        Keyspace.HashExists("h", "zz").Should().BeFalse();
        Keyspace.HashLength("h").Should().Be(2);
        Keyspace.HashLength("missing").Should().Be(0);
        Keyspace.HashStringLength("h", "a").Should().Be(5);
        Keyspace.HashStringLength("h", "zz").Should().Be(0);
    }

    [Fact]
    public void Increment_by_creates_and_adds()
    {
        Keyspace.HashIncrementBy("h", "n", 5).Should().Be(5);
        Keyspace.HashIncrementBy("h", "n", -7).Should().Be(-2);

        Keyspace.HashGet("h", "n").Should().Be("-2");
    }

    [Fact]
    public void Increment_by_rejects_non_integers_and_overflow()
    {
        Keyspace.HashSetFields("h", Fields("text", "abc", "max", long.MaxValue.ToString()));

        Action notInteger = () => Keyspace.HashIncrementBy("h", "text", 1);
        Action overflow = () => Keyspace.HashIncrementBy("h", "max", 1);

        notInteger.Should().Throw<StorageException>().WithMessage("ERR hash value is not an integer");
        overflow.Should().Throw<StorageException>().WithMessage("ERR increment or decrement would overflow");
    }

    [Fact]
    public void Increment_by_float_formats_shortest()
    {
        Keyspace.HashIncrementByFloat("h", "f", 10.5).Should().Be("10.5");
        Keyspace.HashIncrementByFloat("h", "f", 0.1).Should().Be("10.6");
        Keyspace.HashIncrementByFloat("h", "f", -10.6).Should().Be("0");
    }

    [Fact]
    public void Increment_by_float_rejects_bad_values()
    {
        Keyspace.HashSetFields("h", Fields("text", "abc"));
        Keyspace.HashIncrementByFloat("h", "big", double.MaxValue);

        Action notFloat = () => Keyspace.HashIncrementByFloat("h", "text", 1);
        Action infinite = () => Keyspace.HashIncrementByFloat("h", "big", double.MaxValue);

        notFloat.Should().Throw<StorageException>().WithMessage("ERR hash value is not a float");
        infinite.Should().Throw<StorageException>().WithMessage("ERR increment would produce NaN or Infinity");
    }

    [Fact]
    public void Failed_increment_on_missing_key_creates_nothing()
    {
        Action act = () => Keyspace.HashIncrementByFloat("h", "f", double.PositiveInfinity);

        act.Should().Throw<StorageException>();
        Keyspace.Exists("h").Should().BeFalse();
    }

    [Fact]
    public void Expiry_applies_to_the_whole_hash()
    {
        Keyspace.HashSetFields("h", Fields("a", "1"));
        Keyspace.Expire("h", TimeSpan.FromSeconds(1));
        Keyspace.HashSetFields("h", Fields("b", "2"));

        Time.Advance(TimeSpan.FromSeconds(2));

        Keyspace.HashLength("h").Should().Be(0);
    }

    [Fact]
    public void Set_with_no_fields_creates_nothing()
    {
        Keyspace.HashSetFields("h", []).Should().Be(0);

        Keyspace.Exists("h").Should().BeFalse();
    }
}
