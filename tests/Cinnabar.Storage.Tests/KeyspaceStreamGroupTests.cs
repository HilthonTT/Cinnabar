namespace Cinnabar.Storage.Tests;

public class KeyspaceStreamGroupTests : KeyspaceTestBase
{
    private StreamId Add(string id) => Keyspace.StreamAdd("s", id, Fields("f", id));

    private static IEnumerable<string> Ids(IEnumerable<StreamEntry> entries) => entries.Select(e => e.Id.ToString());

    [Fact]
    public void Create_requires_the_stream_unless_mkstream()
    {
        Action act = () => Keyspace.StreamGroupCreate("s", "g", "$");

        act.Should().Throw<StorageException>().WithMessage("ERR The XGROUP subcommand requires the key to exist*");
        Keyspace.StreamGroupCreate("s", "g", "$", makeStream: true).Should().Be(StreamId.Min);
        Keyspace.GetKeyType("s").Should().Be(KeyType.Stream);
        Keyspace.StreamLength("s").Should().Be(0);
    }

    [Fact]
    public void Duplicate_group_is_busygroup()
    {
        Add("1-1");
        Keyspace.StreamGroupCreate("s", "g", "0");

        Action act = () => Keyspace.StreamGroupCreate("s", "g", "0");

        act.Should().Throw<StorageException>().WithMessage("BUSYGROUP Consumer Group name already exists");
    }

    [Fact]
    public void Dollar_starts_after_the_last_entry()
    {
        Add("1-1");

        Keyspace.StreamGroupCreate("s", "g", "$").Should().Be(new StreamId(1, 1));
        Add("2-1");

        Ids(Keyspace.StreamReadGroup("s", "g", "alice", null)).Should().Equal("2-1");
    }

    [Fact]
    public void New_entries_are_delivered_once_across_consumers()
    {
        Add("1-1");
        Add("2-1");
        Add("3-1");
        Keyspace.StreamGroupCreate("s", "g", "0");

        Ids(Keyspace.StreamReadGroup("s", "g", "alice", null, count: 2)).Should().Equal("1-1", "2-1");
        Ids(Keyspace.StreamReadGroup("s", "g", "bob", null)).Should().Equal("3-1");
        Keyspace.StreamReadGroup("s", "g", "bob", null).Should().BeEmpty();
    }

    [Fact]
    public void Pending_entries_are_tracked_until_acknowledged()
    {
        Add("1-1");
        Add("2-1");
        Keyspace.StreamGroupCreate("s", "g", "0");
        Keyspace.StreamReadGroup("s", "g", "alice", null);

        StreamPendingSummary summary = Keyspace.StreamPending("s", "g");
        summary.Count.Should().Be(2);
        summary.Smallest.Should().Be(new StreamId(1, 1));
        summary.Largest.Should().Be(new StreamId(2, 1));
        summary.Consumers.Should().BeEquivalentTo([new KeyValuePair<string, long>("alice", 2)]);

        Keyspace.StreamAck("s", "g", [new StreamId(1, 1), new StreamId(9, 9)]).Should().Be(1);
        Keyspace.StreamPending("s", "g").Count.Should().Be(1);
    }

    [Fact]
    public void Noack_does_not_create_pending_entries()
    {
        Add("1-1");
        Keyspace.StreamGroupCreate("s", "g", "0");

        Keyspace.StreamReadGroup("s", "g", "alice", null, noAck: true).Should().HaveCount(1);

        Keyspace.StreamPending("s", "g").Count.Should().Be(0);
    }

    [Fact]
    public void History_read_returns_own_pending_entries_and_counts_a_delivery()
    {
        Add("1-1");
        Add("2-1");
        Keyspace.StreamGroupCreate("s", "g", "0");
        Keyspace.StreamReadGroup("s", "g", "alice", null);
        Keyspace.StreamAck("s", "g", [new StreamId(1, 1)]);

        Ids(Keyspace.StreamReadGroup("s", "g", "alice", StreamId.Min)).Should().Equal("2-1");
        Keyspace.StreamReadGroup("s", "g", "bob", StreamId.Min).Should().BeEmpty();

        Keyspace.StreamPendingRange("s", "g", StreamId.Min, StreamId.Max, 10)
            .Should().ContainSingle().Which.DeliveryCount.Should().Be(2);
    }

    [Fact]
    public void Pending_range_filters_by_consumer_and_idle_time()
    {
        Add("1-1");
        Add("2-1");
        Keyspace.StreamGroupCreate("s", "g", "0");
        Keyspace.StreamReadGroup("s", "g", "alice", null, count: 1);
        Time.Advance(TimeSpan.FromSeconds(5));
        Keyspace.StreamReadGroup("s", "g", "bob", null, count: 1);

        IReadOnlyList<StreamPendingEntry> all = Keyspace.StreamPendingRange("s", "g", StreamId.Min, StreamId.Max, 10);
        all.Select(e => (e.Consumer, e.IdleMilliseconds)).Should().Equal(("alice", 5000L), ("bob", 0L));

        Keyspace.StreamPendingRange("s", "g", StreamId.Min, StreamId.Max, 10, "bob").Should().ContainSingle();
        Keyspace.StreamPendingRange("s", "g", StreamId.Min, StreamId.Max, 10, minIdleMilliseconds: 1000)
            .Should().ContainSingle().Which.Consumer.Should().Be("alice");
        Keyspace.StreamPendingRange("s", "g", StreamId.Min, StreamId.Max, 1).Should().ContainSingle();
    }

    [Fact]
    public void Claim_moves_idle_entries_only()
    {
        Add("1-1");
        Add("2-1");
        Keyspace.StreamGroupCreate("s", "g", "0");
        Keyspace.StreamReadGroup("s", "g", "alice", null, count: 1);
        Time.Advance(TimeSpan.FromSeconds(10));
        Keyspace.StreamReadGroup("s", "g", "alice", null, count: 1);

        StreamClaimResult result = Keyspace.StreamClaim("s", "g", "bob", 5_000, [new StreamId(1, 1), new StreamId(2, 1)]);

        Ids(result.Entries).Should().Equal("1-1");
        Keyspace.StreamPending("s", "g").Consumers.Should().BeEquivalentTo(
            [new KeyValuePair<string, long>("alice", 1), new KeyValuePair<string, long>("bob", 1)]);
        Keyspace.StreamPendingRange("s", "g", StreamId.Min, StreamId.Max, 10, "bob")
            .Single().DeliveryCount.Should().Be(2);
    }

    [Fact]
    public void Claim_options_force_justid_retrycount_lastid_and_time()
    {
        Add("1-1");
        Add("2-1");
        Keyspace.StreamGroupCreate("s", "g", "0");

        StreamClaimResult forced = Keyspace.StreamClaim(
            "s", "g", "bob", 0, [new StreamId(2, 1), new StreamId(7, 7)],
            new StreamClaimOptions(Force: true, RetryCount: 5, TimeMilliseconds: NowMs - 3_000, LastId: new StreamId(2, 1)));

        Ids(forced.Entries).Should().Equal("2-1");
        StreamPendingEntry entry = Keyspace.StreamPendingRange("s", "g", StreamId.Min, StreamId.Max, 10).Single();
        entry.DeliveryCount.Should().Be(5);
        entry.IdleMilliseconds.Should().Be(3_000);
        Keyspace.StreamReadGroup("s", "g", "alice", null).Should().BeEmpty("LASTID moved the group past 2-1");

        Keyspace.StreamClaim("s", "g", "carol", 0, [new StreamId(2, 1)], new StreamClaimOptions(JustId: true));
        Keyspace.StreamPendingRange("s", "g", StreamId.Min, StreamId.Max, 10).Single().DeliveryCount.Should().Be(5);
    }

    [Fact]
    public void Autoclaim_pages_through_the_pending_list()
    {
        for (int i = 1; i <= 5; i++)
        {
            Add($"{i}-1");
        }

        Keyspace.StreamGroupCreate("s", "g", "0");
        Keyspace.StreamReadGroup("s", "g", "alice", null);
        Time.Advance(TimeSpan.FromMinutes(1));

        StreamClaimResult first = Keyspace.StreamAutoClaim("s", "g", "bob", 1_000, StreamId.Min, 2);
        Ids(first.Entries).Should().Equal("1-1", "2-1");
        first.NextCursor.Should().Be(new StreamId(3, 1));

        StreamClaimResult rest = Keyspace.StreamAutoClaim("s", "g", "bob", 1_000, first.NextCursor, 10);
        Ids(rest.Entries).Should().Equal("3-1", "4-1", "5-1");
        rest.NextCursor.Should().Be(StreamId.Min);
    }

    [Fact]
    public void Consumers_can_be_created_and_deleted()
    {
        Add("1-1");
        Keyspace.StreamGroupCreate("s", "g", "0");

        Keyspace.StreamGroupCreateConsumer("s", "g", "alice").Should().BeTrue();
        Keyspace.StreamGroupCreateConsumer("s", "g", "alice").Should().BeFalse();
        Keyspace.StreamReadGroup("s", "g", "alice", null);

        Keyspace.StreamGroupDeleteConsumer("s", "g", "alice").Should().Be(1);
        Keyspace.StreamPending("s", "g").Count.Should().Be(0);
        Keyspace.StreamGroupDeleteConsumer("s", "g", "nobody").Should().Be(0);
    }

    [Fact]
    public void Setid_and_destroy()
    {
        Add("1-1");
        Add("2-1");
        Keyspace.StreamGroupCreate("s", "g", "$");

        Keyspace.StreamGroupSetId("s", "g", "1-1").Should().Be(new StreamId(1, 1));
        Ids(Keyspace.StreamReadGroup("s", "g", "alice", null)).Should().Equal("2-1");

        Keyspace.StreamGroupDestroy("s", "g").Should().BeTrue();
        Keyspace.StreamGroupDestroy("s", "g").Should().BeFalse();
        Keyspace.StreamGroupExists("s", "g").Should().BeFalse();
    }

    [Fact]
    public void Missing_group_is_nogroup()
    {
        Add("1-1");

        Action read = () => Keyspace.StreamReadGroup("s", "nope", "alice", null);
        Action pending = () => Keyspace.StreamPending("s", "nope");

        read.Should().Throw<StorageException>().WithMessage("NOGROUP *XREADGROUP*");
        pending.Should().Throw<StorageException>().WithMessage("NOGROUP No such key 's' or consumer group 'nope'");
        Keyspace.StreamAck("s", "nope", [new StreamId(1, 1)]).Should().Be(0);
    }

    [Fact]
    public void Group_commands_on_other_types_are_wrongtype()
    {
        Keyspace.SetString("str", "v");

        Action act = () => Keyspace.StreamGroupCreate("str", "g", "$", makeStream: true);

        act.Should().Throw<WrongTypeException>();
    }

    [Fact]
    public void Reading_with_nothing_new_does_not_count_as_a_change()
    {
        Add("1-1");
        Keyspace.StreamGroupCreate("s", "g", "$");
        Keyspace.StreamGroupCreateConsumer("s", "g", "alice");
        long version = Keyspace.Watch("s");

        Keyspace.StreamReadGroup("s", "g", "alice", null);

        Keyspace.IsUnchanged("s", version).Should().BeTrue();
    }
}
