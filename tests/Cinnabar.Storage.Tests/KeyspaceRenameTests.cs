namespace Cinnabar.Storage.Tests;

public class KeyspaceRenameTests : KeyspaceTestBase
{
    [Fact]
    public void Rename_moves_value_and_ttl()
    {
        Keyspace.SetString("a", "v", expiry: TimeSpan.FromSeconds(30));

        Keyspace.Rename("a", "b").Should().BeTrue();

        Keyspace.Exists("a").Should().BeFalse();
        Keyspace.GetString("b").Should().Be("v");
        Keyspace.GetTimeToLiveMilliseconds("b").Should().Be(30_000);
    }

    [Fact]
    public void Rename_overwrites_destination_of_any_type()
    {
        Keyspace.ListPush("a", ["x"], ListEnd.Right);
        Keyspace.SetString("b", "old", expiry: TimeSpan.FromSeconds(5));

        Keyspace.Rename("a", "b");

        Keyspace.GetKeyType("b").Should().Be(KeyType.List);
        Keyspace.GetTimeToLiveMilliseconds("b").Should().Be(-1);
    }

    [Fact]
    public void Rename_of_missing_key_is_an_error()
    {
        FluentActions.Invoking(() => Keyspace.Rename("missing", "b"))
            .Should().Throw<StorageException>().WithMessage("ERR no such key");
    }

    [Fact]
    public void Rename_to_itself_keeps_the_key()
    {
        Keyspace.SetString("a", "v");

        Keyspace.Rename("a", "a").Should().BeTrue();
        Keyspace.Rename("a", "a", onlyIfNotExists: true).Should().BeFalse();

        Keyspace.GetString("a").Should().Be("v");
    }

    [Fact]
    public void RenameNx_does_not_overwrite()
    {
        Keyspace.SetString("a", "1");
        Keyspace.SetString("b", "2");

        Keyspace.Rename("a", "b", onlyIfNotExists: true).Should().BeFalse();

        Keyspace.GetString("a").Should().Be("1");
        Keyspace.GetString("b").Should().Be("2");
    }

    [Fact]
    public void Rename_changes_watched_versions_of_both_keys()
    {
        Keyspace.SetString("a", "1");
        long source = Keyspace.Watch("a");
        long destination = Keyspace.Watch("b");

        Keyspace.Rename("a", "b");

        Keyspace.IsUnchanged("a", source).Should().BeFalse();
        Keyspace.IsUnchanged("b", destination).Should().BeFalse();
    }

    [Fact]
    public void IncrementByFloat_adds_and_keeps_ttl()
    {
        Keyspace.SetString("n", "10.5", expiry: TimeSpan.FromSeconds(10));

        Keyspace.IncrementByFloat("n", 0.1).Should().Be("10.6");
        Keyspace.IncrementByFloat("n", -5).Should().Be("5.6");

        Keyspace.GetTimeToLiveMilliseconds("n").Should().Be(10_000);
    }

    [Fact]
    public void IncrementByFloat_creates_missing_key()
    {
        Keyspace.IncrementByFloat("n", 2.5).Should().Be("2.5");
        Keyspace.GetString("n").Should().Be("2.5");
    }

    [Fact]
    public void IncrementByFloat_rejects_non_numbers_and_other_types()
    {
        Keyspace.SetString("s", "abc");
        Keyspace.ListPush("l", ["x"], ListEnd.Right);

        FluentActions.Invoking(() => Keyspace.IncrementByFloat("s", 1))
            .Should().Throw<StorageException>().WithMessage("ERR value is not a valid float");
        FluentActions.Invoking(() => Keyspace.IncrementByFloat("l", 1)).Should().Throw<WrongTypeException>();
    }

    [Fact]
    public void IncrementByFloat_rejects_overflow_to_infinity()
    {
        Keyspace.SetString("n", "1e308");

        FluentActions.Invoking(() => Keyspace.IncrementByFloat("n", 1e308))
            .Should().Throw<StorageException>().WithMessage("ERR increment would produce NaN or Infinity");
        Keyspace.GetString("n").Should().Be("1e308");
    }
}
