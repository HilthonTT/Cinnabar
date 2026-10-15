namespace Cinnabar.Storage.Tests;

public class KeyspaceStreamTests : KeyspaceTestBase
{
    private const string TooSmall = "ERR The ID specified in XADD is equal or smaller than the target stream top item";
    private const string ZeroId = "ERR The ID specified in XADD must be greater than 0-0";
    private const string InvalidId = "ERR Invalid stream ID specified as stream command argument";

    private ulong Now => (ulong)NowMs;

    [Fact]
    public void Add_with_star_uses_current_time_and_sequence_zero()
    {
        StreamId id = Keyspace.StreamAdd("s", "*", Fields("f", "v"));

        id.Should().Be(new StreamId(Now, 0));
    }

    [Fact]
    public void Add_with_star_in_same_millisecond_increments_sequence()
    {
        Keyspace.StreamAdd("s", "*", Fields("f", "1"));

        StreamId second = Keyspace.StreamAdd("s", "*", Fields("f", "2"));

        second.Should().Be(new StreamId(Now, 1));
    }

    [Fact]
    public void Add_with_star_after_time_advances_resets_sequence()
    {
        Keyspace.StreamAdd("s", "*", Fields("f", "1"));
        Keyspace.StreamAdd("s", "*", Fields("f", "2"));
        Time.Advance(TimeSpan.FromMilliseconds(5));

        Keyspace.StreamAdd("s", "*", Fields("f", "3")).Should().Be(new StreamId(Now, 0));
    }

    [Fact]
    public void Add_with_star_still_increases_when_clock_is_behind_top_id()
    {
        // The top entry is in the future relative to the clock (clock went backwards).
        ulong future = Now + 10_000;
        Keyspace.StreamAdd("s", $"{future}-5", Fields("f", "1"));

        StreamId id = Keyspace.StreamAdd("s", "*", Fields("f", "2"));

        id.Should().Be(new StreamId(future, 6));
    }

    [Fact]
    public void Add_with_ms_star_on_empty_stream_uses_sequence_zero()
    {
        Keyspace.StreamAdd("s", "5-*", Fields("f", "v")).Should().Be(new StreamId(5, 0));
    }

    [Fact]
    public void Add_with_zero_star_on_empty_stream_uses_sequence_one()
    {
        Keyspace.StreamAdd("s", "0-*", Fields("f", "v")).Should().Be(new StreamId(0, 1));
    }

    [Fact]
    public void Add_with_ms_star_same_ms_increments_sequence()
    {
        Keyspace.StreamAdd("s", "5-3", Fields("f", "v"));

        Keyspace.StreamAdd("s", "5-*", Fields("f", "v")).Should().Be(new StreamId(5, 4));
    }

    [Fact]
    public void Add_with_ms_star_greater_ms_uses_sequence_zero()
    {
        Keyspace.StreamAdd("s", "5-3", Fields("f", "v"));

        Keyspace.StreamAdd("s", "6-*", Fields("f", "v")).Should().Be(new StreamId(6, 0));
    }

    [Fact]
    public void Add_with_ms_star_smaller_ms_is_rejected()
    {
        Keyspace.StreamAdd("s", "5-3", Fields("f", "v"));

        Action act = () => Keyspace.StreamAdd("s", "4-*", Fields("f", "v"));

        act.Should().Throw<StorageException>().WithMessage(TooSmall);
    }

    [Fact]
    public void Add_with_explicit_id()
    {
        Keyspace.StreamAdd("s", "1526919030474-55", Fields("f", "v")).Should().Be(new StreamId(1526919030474, 55));
    }

    [Fact]
    public void Add_with_bare_ms_uses_sequence_zero()
    {
        Keyspace.StreamAdd("s", "7", Fields("f", "v")).Should().Be(new StreamId(7, 0));
    }

    [Fact]
    public void Add_with_zero_zero_is_rejected()
    {
        Action act = () => Keyspace.StreamAdd("s", "0-0", Fields("f", "v"));

        act.Should().Throw<StorageException>().WithMessage(ZeroId);
    }

    [Theory]
    [InlineData("5-3")]
    [InlineData("5-2")]
    [InlineData("4-9")]
    [InlineData("5")]
    public void Add_with_equal_or_smaller_id_is_rejected(string id)
    {
        Keyspace.StreamAdd("s", "5-3", Fields("f", "v"));

        Action act = () => Keyspace.StreamAdd("s", id, Fields("f", "v"));

        act.Should().Throw<StorageException>().WithMessage(TooSmall);
        Keyspace.StreamLength("s").Should().Be(1);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1-x")]
    [InlineData("-1")]
    [InlineData("1-")]
    [InlineData("1-2-3")]
    [InlineData("x-*")]
    [InlineData("")]
    public void Add_with_malformed_id_is_rejected(string id)
    {
        Action act = () => Keyspace.StreamAdd("s", id, Fields("f", "v"));

        act.Should().Throw<StorageException>().WithMessage(InvalidId);
    }

    [Theory]
    [InlineData("0-0")]
    [InlineData("abc")]
    public void Rejected_id_on_missing_key_does_not_create_key(string id)
    {
        Action act = () => Keyspace.StreamAdd("s", id, Fields("f", "v"));

        act.Should().Throw<StorageException>();
        Keyspace.Exists("s").Should().BeFalse();
        Keyspace.Count.Should().Be(0);
    }

    [Fact]
    public void Added_entry_keeps_fields_in_order()
    {
        Keyspace.StreamAdd("s", "1-1", Fields("b", "2", "a", "1"));

        StreamEntry entry = Keyspace.StreamRange("s", StreamId.Min, StreamId.Max).Single();

        entry.Fields.Select(f => f.Key).Should().Equal("b", "a");
        entry.Fields.Select(f => f.Value).Should().Equal("2", "1");
    }

    [Fact]
    public void Range_returns_entries_within_inclusive_bounds()
    {
        AddIds("1-0", "2-0", "2-1", "3-0", "4-0");

        IReadOnlyList<StreamEntry> range = Keyspace.StreamRange("s", new StreamId(2, 0), new StreamId(3, 0));

        range.Select(e => e.Id.ToString()).Should().Equal("2-0", "2-1", "3-0");
    }

    [Fact]
    public void Range_with_count_limits_results()
    {
        AddIds("1-0", "2-0", "3-0");

        Keyspace.StreamRange("s", StreamId.Min, StreamId.Max, count: 2)
            .Select(e => e.Id.ToString()).Should().Equal("1-0", "2-0");
    }

    [Fact]
    public void Range_with_zero_count_is_empty()
    {
        AddIds("1-0");

        Keyspace.StreamRange("s", StreamId.Min, StreamId.Max, count: 0).Should().BeEmpty();
    }

    [Fact]
    public void Range_with_start_after_end_is_empty()
    {
        AddIds("1-0", "2-0");

        Keyspace.StreamRange("s", new StreamId(2, 0), new StreamId(1, 0)).Should().BeEmpty();
    }

    [Fact]
    public void Range_between_entries_is_empty()
    {
        AddIds("1-0", "5-0");

        Keyspace.StreamRange("s", new StreamId(2, 0), new StreamId(4, 0)).Should().BeEmpty();
    }

    [Fact]
    public void Range_with_parsed_exclusive_bounds()
    {
        AddIds("1-0", "2-0", "3-0", "4-0");
        StreamId.TryParseRangeBound("(1-0", isStart: true, out StreamId? start).Should().BeTrue();
        StreamId.TryParseRangeBound("(4-0", isStart: false, out StreamId? end).Should().BeTrue();

        Keyspace.StreamRange("s", start!.Value, end!.Value).Select(e => e.Id.ToString()).Should().Equal("2-0", "3-0");
    }

    [Fact]
    public void Range_with_bare_ms_bounds_covers_whole_millisecond()
    {
        AddIds("1-5", "2-0", "2-7", "3-0");
        StreamId.TryParseRangeBound("2", isStart: true, out StreamId? start);
        StreamId.TryParseRangeBound("2", isStart: false, out StreamId? end);

        Keyspace.StreamRange("s", start!.Value, end!.Value).Select(e => e.Id.ToString()).Should().Equal("2-0", "2-7");
    }

    [Fact]
    public void Range_of_missing_key_is_empty()
    {
        Keyspace.StreamRange("missing", StreamId.Min, StreamId.Max).Should().BeEmpty();
    }

    [Fact]
    public void ReadAfter_returns_entries_strictly_after_id()
    {
        AddIds("1-0", "2-0", "3-0");

        Keyspace.StreamReadAfter("s", new StreamId(2, 0)).Select(e => e.Id.ToString()).Should().Equal("3-0");
    }

    [Fact]
    public void ReadAfter_min_returns_everything()
    {
        AddIds("0-1", "2-0");

        Keyspace.StreamReadAfter("s", StreamId.Min).Should().HaveCount(2);
    }

    [Fact]
    public void ReadAfter_with_count()
    {
        AddIds("1-0", "2-0", "3-0");

        Keyspace.StreamReadAfter("s", StreamId.Min, count: 1).Select(e => e.Id.ToString()).Should().Equal("1-0");
    }

    [Fact]
    public void ReadAfter_last_id_is_empty()
    {
        AddIds("1-0", "2-0");

        Keyspace.StreamReadAfter("s", new StreamId(2, 0)).Should().BeEmpty();
    }

    [Fact]
    public void ReadAfter_max_is_empty()
    {
        AddIds("1-0");

        Keyspace.StreamReadAfter("s", StreamId.Max).Should().BeEmpty();
    }

    [Fact]
    public void ReadAfter_missing_key_is_empty()
    {
        Keyspace.StreamReadAfter("missing", StreamId.Min).Should().BeEmpty();
    }

    [Fact]
    public void LastId_of_missing_stream_is_null()
    {
        Keyspace.StreamLastId("missing").Should().BeNull();
    }

    [Fact]
    public void LastId_is_newest_entry()
    {
        AddIds("1-0", "9-4");

        Keyspace.StreamLastId("s").Should().Be(new StreamId(9, 4));
    }

    [Fact]
    public void Length_counts_entries()
    {
        Keyspace.StreamLength("s").Should().Be(0);

        AddIds("1-0", "2-0", "3-0");

        Keyspace.StreamLength("s").Should().Be(3);
    }

    [Fact]
    public void Stream_key_has_type_stream()
    {
        AddIds("1-0");

        Keyspace.GetKeyType("s").Should().Be(KeyType.Stream);
    }

    private void AddIds(params string[] ids)
    {
        foreach (string id in ids)
        {
            Keyspace.StreamAdd("s", id, Fields("id", id));
        }
    }
}
