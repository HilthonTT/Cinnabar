using Microsoft.Extensions.Time.Testing;

namespace Cinnabar.Storage.Tests;

public class KeyspaceSnapshotTests : KeyspaceTestBase
{
    [Fact]
    public void Snapshot_copies_every_type_with_expiry()
    {
        Keyspace.SetString("s", "v", expiry: TimeSpan.FromSeconds(10));
        Keyspace.ListPush("l", ["a", "b"], ListEnd.Right);
        Keyspace.StreamAdd("x", "1-1", Fields("f", "v"));

        var snapshot = Keyspace.Snapshot().ToDictionary(k => k.Key);

        snapshot.Should().HaveCount(3);
        snapshot["s"].Type.Should().Be(KeyType.String);
        snapshot["s"].Value.Should().Be("v");
        snapshot["s"].ExpiresAt.Should().Be(Time.GetUtcNow().AddSeconds(10));
        snapshot["l"].Type.Should().Be(KeyType.List);
        ((IEnumerable<string>)snapshot["l"].Value).Should().Equal("a", "b");
        snapshot["l"].ExpiresAt.Should().BeNull();
        snapshot["x"].Type.Should().Be(KeyType.Stream);
        ((IEnumerable<StreamEntry>)snapshot["x"].Value).Single().Id.Should().Be(new StreamId(1, 1));
    }

    [Fact]
    public void Snapshot_is_a_copy()
    {
        Keyspace.ListPush("l", ["a"], ListEnd.Right);
        KeySnapshot snapshot = Keyspace.Snapshot().Single();

        Keyspace.ListPush("l", ["b"], ListEnd.Right);

        ((IEnumerable<string>)snapshot.Value).Should().Equal("a");
    }

    [Fact]
    public void Snapshot_then_load_round_trips()
    {
        Keyspace.SetString("s", "v", expiry: TimeSpan.FromSeconds(10));
        Keyspace.ListPush("l", ["a", "b"], ListEnd.Right);
        Keyspace.StreamAdd("x", "1-1", Fields("f", "v"));
        Keyspace.StreamAdd("x", "2-0", Fields("g", "w"));

        using var target = new Keyspace(Time, TimeSpan.Zero);
        target.Load(Keyspace.Snapshot()).Should().Be(3);

        target.GetString("s").Should().Be("v");
        target.GetTimeToLiveMilliseconds("s").Should().Be(10_000);
        target.ListRange("l", 0, -1).Should().Equal("a", "b");
        target.StreamRange("x", StreamId.Min, StreamId.Max).Select(e => e.Id.ToString()).Should().Equal("1-1", "2-0");
        target.StreamAdd("x", "*", Fields("h", "z")).CompareTo(new StreamId(2, 0)).Should().BePositive();
    }

    [Fact]
    public void Load_skips_keys_that_already_expired()
    {
        DateTimeOffset now = Time.GetUtcNow();
        KeySnapshot[] keys =
        [
            new("past", KeyType.String, "v", now.AddSeconds(-1)),
            new("exactly-now", KeyType.String, "v", now),
            new("future", KeyType.String, "v", now.AddSeconds(1)),
            new("forever", KeyType.String, "v", null),
        ];

        Keyspace.Load(keys).Should().Be(2);

        Keyspace.Keys().Should().Equal("forever", "future");
        Keyspace.Count.Should().Be(2);
    }

    [Fact]
    public void Load_replaces_existing_key_of_another_type()
    {
        Keyspace.ListPush("k", ["a"], ListEnd.Right);

        Keyspace.Load([new KeySnapshot("k", KeyType.String, "v", null)]);

        Keyspace.GetString("k").Should().Be("v");
    }

    [Fact]
    public void Load_rejects_none_type()
    {
        Action act = () => Keyspace.Load([new KeySnapshot("k", KeyType.None, "v", null)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Load_rejects_out_of_order_stream_entries()
    {
        StreamEntry[] entries =
        [
            new(new StreamId(2, 0), Fields("f", "v")),
            new(new StreamId(1, 0), Fields("f", "v")),
        ];

        Action act = () => Keyspace.Load([new KeySnapshot("x", KeyType.Stream, entries, null)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Loaded_expiry_is_honoured()
    {
        Keyspace.Load([new KeySnapshot("k", KeyType.String, "v", Time.GetUtcNow().AddSeconds(5))]);

        Time.Advance(TimeSpan.FromSeconds(5));

        Keyspace.Exists("k").Should().BeFalse();
    }

    [Fact]
    public void Load_into_keyspace_with_later_clock_skips_keys_expired_in_between()
    {
        Keyspace.SetString("short", "v", expiry: TimeSpan.FromSeconds(1));
        Keyspace.SetString("long", "v", expiry: TimeSpan.FromHours(1));
        IReadOnlyList<KeySnapshot> snapshot = Keyspace.Snapshot();

        var later = new FakeTimeProvider(Time.GetUtcNow().AddMinutes(1));
        using var target = new Keyspace(later, TimeSpan.Zero);

        target.Load(snapshot).Should().Be(1);
        target.Keys().Should().Equal("long");
    }
}
