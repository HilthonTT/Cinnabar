namespace Cinnabar.Storage.Tests;

public class StreamIdTests
{
    [Theory]
    [InlineData("0-0", 0UL, 0UL)]
    [InlineData("1526919030474-55", 1526919030474UL, 55UL)]
    [InlineData("18446744073709551615-18446744073709551615", ulong.MaxValue, ulong.MaxValue)]
    public void TryParse_full_id(string text, ulong ms, ulong seq)
    {
        StreamId.TryParse(text, 99, out StreamId id).Should().BeTrue();

        id.Should().Be(new StreamId(ms, seq));
    }

    [Fact]
    public void TryParse_bare_ms_uses_default_sequence()
    {
        StreamId.TryParse("42", 7, out StreamId id).Should().BeTrue();

        id.Should().Be(new StreamId(42, 7));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("abc")]
    [InlineData("1-")]
    [InlineData("-1")]
    [InlineData("1-2-3")]
    [InlineData("1-a")]
    [InlineData(" 1-2")]
    [InlineData("+1-2")]
    [InlineData("18446744073709551616-0")]
    [InlineData("1-18446744073709551616")]
    public void TryParse_rejects_malformed(string text)
    {
        StreamId.TryParse(text, 0, out _).Should().BeFalse();
    }

    [Fact]
    public void ToString_is_ms_dash_seq()
    {
        new StreamId(12, 3).ToString().Should().Be("12-3");
    }

    [Fact]
    public void Range_bound_minus_is_min()
    {
        StreamId.TryParseRangeBound("-", isStart: true, out StreamId? id).Should().BeTrue();

        id.Should().Be(StreamId.Min);
    }

    [Fact]
    public void Range_bound_plus_is_max()
    {
        StreamId.TryParseRangeBound("+", isStart: false, out StreamId? id).Should().BeTrue();

        id.Should().Be(StreamId.Max);
    }

    [Fact]
    public void Range_bound_bare_ms_as_start_has_sequence_zero()
    {
        StreamId.TryParseRangeBound("5", isStart: true, out StreamId? id).Should().BeTrue();

        id.Should().Be(new StreamId(5, 0));
    }

    [Fact]
    public void Range_bound_bare_ms_as_end_has_max_sequence()
    {
        StreamId.TryParseRangeBound("5", isStart: false, out StreamId? id).Should().BeTrue();

        id.Should().Be(new StreamId(5, ulong.MaxValue));
    }

    [Fact]
    public void Range_bound_full_id_is_inclusive()
    {
        StreamId.TryParseRangeBound("5-3", isStart: true, out StreamId? start);
        StreamId.TryParseRangeBound("5-3", isStart: false, out StreamId? end);

        start.Should().Be(new StreamId(5, 3));
        end.Should().Be(new StreamId(5, 3));
    }

    [Fact]
    public void Exclusive_start_is_next_id()
    {
        StreamId.TryParseRangeBound("(5-3", isStart: true, out StreamId? id).Should().BeTrue();

        id.Should().Be(new StreamId(5, 4));
    }

    [Fact]
    public void Exclusive_end_is_previous_id()
    {
        StreamId.TryParseRangeBound("(5-3", isStart: false, out StreamId? id).Should().BeTrue();

        id.Should().Be(new StreamId(5, 2));
    }

    [Fact]
    public void Exclusive_bare_ms_start_skips_sequence_zero()
    {
        StreamId.TryParseRangeBound("(5", isStart: true, out StreamId? id).Should().BeTrue();

        id.Should().Be(new StreamId(5, 1));
    }

    [Fact]
    public void Exclusive_bare_ms_end_rolls_into_previous_sequence()
    {
        StreamId.TryParseRangeBound("(5", isStart: false, out StreamId? id).Should().BeTrue();

        id.Should().Be(new StreamId(5, ulong.MaxValue - 1));
    }

    [Fact]
    public void Exclusive_start_at_max_saturates()
    {
        StreamId.TryParseRangeBound("(+", isStart: true, out StreamId? id).Should().BeTrue();

        id.Should().Be(StreamId.Max);
    }

    [Fact]
    public void Exclusive_end_at_min_saturates()
    {
        StreamId.TryParseRangeBound("(-", isStart: false, out StreamId? id).Should().BeTrue();

        id.Should().Be(StreamId.Min);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(")]
    [InlineData("abc")]
    [InlineData("((1")]
    [InlineData("1-x")]
    public void Range_bound_rejects_malformed(string text)
    {
        StreamId.TryParseRangeBound(text, isStart: true, out StreamId? id).Should().BeFalse();

        id.Should().BeNull();
    }

    [Fact]
    public void Next_increments_sequence()
    {
        new StreamId(5, 3).Next().Should().Be(new StreamId(5, 4));
    }

    [Fact]
    public void Next_rolls_over_to_next_millisecond()
    {
        new StreamId(5, ulong.MaxValue).Next().Should().Be(new StreamId(6, 0));
    }

    [Fact]
    public void Next_of_max_is_null()
    {
        StreamId.Max.Next().Should().BeNull();
    }

    [Fact]
    public void Previous_decrements_sequence()
    {
        new StreamId(5, 3).Previous().Should().Be(new StreamId(5, 2));
    }

    [Fact]
    public void Previous_rolls_back_to_previous_millisecond()
    {
        new StreamId(5, 0).Previous().Should().Be(new StreamId(4, ulong.MaxValue));
    }

    [Fact]
    public void Previous_of_min_is_null()
    {
        StreamId.Min.Previous().Should().BeNull();
    }

    [Fact]
    public void Ordering_compares_milliseconds_then_sequence()
    {
        StreamId[] ids = [new(2, 0), new(1, 5), new(1, 0), new(10, 0), new(1, 10)];

        ids.Order().Select(i => i.ToString()).Should().Equal("1-0", "1-5", "1-10", "2-0", "10-0");
    }

    [Fact]
    public void CompareTo_equal_ids_is_zero()
    {
        new StreamId(3, 4).CompareTo(new StreamId(3, 4)).Should().Be(0);
        new StreamId(3, 4).Should().Be(new StreamId(3, 4));
    }

    [Fact]
    public void Min_and_max_bound_every_id()
    {
        var id = new StreamId(123, 456);

        StreamId.Min.CompareTo(id).Should().BeNegative();
        StreamId.Max.CompareTo(id).Should().BePositive();
    }
}
