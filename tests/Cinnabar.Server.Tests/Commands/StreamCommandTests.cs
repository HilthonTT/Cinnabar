using Cinnabar.Server.Blocking;
using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class StreamCommandTests : DispatcherTestBase
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static string Entry(string id, params string[] fields) =>
        $"*2\r\n{Bulk(id)}{BulkArray(fields)}";

    private static string Entries(params string[] entries) => $"*{entries.Length}\r\n" + string.Concat(entries);

    // ───────────── XADD ─────────────

    [Fact]
    public async Task Xadd_with_explicit_id_returns_it()
    {
        (await Run("XADD", "s", "1-1", "f", "v")).Should().Be(Bulk("1-1"));
    }

    [Fact]
    public async Task Xadd_with_star_uses_current_time()
    {
        (await Run("XADD", "s", "*", "f", "v")).Should().Be(Bulk($"{H.NowMs}-0"));
        (await Run("XADD", "s", "*", "f", "v")).Should().Be(Bulk($"{H.NowMs}-1"));
    }

    [Fact]
    public async Task Xadd_with_partial_id_generates_sequence()
    {
        (await Run("XADD", "s", "5-*", "f", "v")).Should().Be(Bulk("5-0"));
        (await Run("XADD", "s", "5-*", "f", "v")).Should().Be(Bulk("5-1"));
        (await Run("XADD", "s", "6-*", "f", "v")).Should().Be(Bulk("6-0"));
    }

    [Fact]
    public async Task Xadd_with_zero_ms_partial_id_starts_at_one()
    {
        (await Run("XADD", "s", "0-*", "f", "v")).Should().Be(Bulk("0-1"));
    }

    [Fact]
    public async Task Xadd_with_bare_milliseconds_uses_sequence_zero()
    {
        (await Run("XADD", "s", "7", "f", "v")).Should().Be(Bulk("7-0"));
    }

    [Fact]
    public async Task Xadd_rejects_id_zero()
    {
        (await Run("XADD", "s", "0-0", "f", "v")).Should().Be("-ERR The ID specified in XADD must be greater than 0-0\r\n");
        (await Run("EXISTS", "s")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Xadd_rejects_id_not_greater_than_top()
    {
        await Run("XADD", "s", "5-5", "f", "v");

        (await Run("XADD", "s", "5-5", "f", "v")).Should().StartWith("-ERR The ID specified in XADD is equal or smaller");
        (await Run("XADD", "s", "4-9", "f", "v")).Should().StartWith("-ERR The ID specified in XADD is equal or smaller");
    }

    [Fact]
    public async Task Xadd_rejects_malformed_id()
    {
        (await Run("XADD", "s", "abc", "f", "v")).Should().StartWith("-ERR");
    }

    [Fact]
    public async Task Xadd_with_odd_field_count_is_an_arity_error()
    {
        (await Run("XADD", "s", "*", "f", "v", "g")).Should().Be("-ERR wrong number of arguments for 'xadd' command\r\n");
        (await Run("EXISTS", "s")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Xadd_on_string_is_wrongtype()
    {
        await Run("SET", "k", "v");

        (await Run("XADD", "k", "*", "f", "v")).Should().StartWith("-WRONGTYPE");
    }

    [Fact]
    public async Task Xadd_propagates_with_generated_id()
    {
        Reply reply = await H.Dispatch("XADD", "s", "*", "f", "v", "g", "w");

        reply.PropagateAs!.ToString().Should().Be($"XADD s {H.NowMs}-0 f v g w");
    }

    // ───────────── XLEN / XRANGE ─────────────

    [Fact]
    public async Task Xlen_counts_entries()
    {
        (await Run("XLEN", "s")).Should().Be(":0\r\n");
        await Run("XADD", "s", "1", "f", "v");
        await Run("XADD", "s", "2", "f", "v");

        (await Run("XLEN", "s")).Should().Be(":2\r\n");
    }

    private async Task AddThree()
    {
        await Run("XADD", "s", "1-0", "a", "1");
        await Run("XADD", "s", "2-0", "b", "2");
        await Run("XADD", "s", "3-0", "c", "3");
    }

    [Fact]
    public async Task Xrange_full_range_returns_all_entries()
    {
        await AddThree();

        (await Run("XRANGE", "s", "-", "+")).Should().Be(
            Entries(Entry("1-0", "a", "1"), Entry("2-0", "b", "2"), Entry("3-0", "c", "3")));
    }

    [Fact]
    public async Task Xrange_bounds_are_inclusive()
    {
        await AddThree();

        (await Run("XRANGE", "s", "2-0", "3-0")).Should().Be(Entries(Entry("2-0", "b", "2"), Entry("3-0", "c", "3")));
    }

    [Fact]
    public async Task Xrange_exclusive_bounds()
    {
        await AddThree();

        (await Run("XRANGE", "s", "(1-0", "(3-0")).Should().Be(Entries(Entry("2-0", "b", "2")));
    }

    [Fact]
    public async Task Xrange_with_count_limits_results()
    {
        await AddThree();

        (await Run("XRANGE", "s", "-", "+", "COUNT", "2")).Should().Be(
            Entries(Entry("1-0", "a", "1"), Entry("2-0", "b", "2")));
    }

    [Fact]
    public async Task Xrange_bare_millisecond_end_includes_all_sequences()
    {
        await Run("XADD", "s", "5-0", "a", "1");
        await Run("XADD", "s", "5-1", "a", "2");

        (await Run("XRANGE", "s", "-", "5")).Should().Be(Entries(Entry("5-0", "a", "1"), Entry("5-1", "a", "2")));
    }

    [Fact]
    public async Task Xrange_on_missing_key_is_empty()
    {
        (await Run("XRANGE", "missing", "-", "+")).Should().Be("*0\r\n");
    }

    [Theory]
    [InlineData("-", "+", "LIMIT", "2")]
    [InlineData("-", "+", "COUNT")]
    public async Task Xrange_with_bad_options_is_a_syntax_error(params string[] args)
    {
        (await Run(["XRANGE", "s", .. args])).Should().Be("-ERR syntax error\r\n");
    }

    [Fact]
    public async Task Xrange_with_invalid_id_is_an_error()
    {
        (await Run("XRANGE", "s", "x", "+")).Should().Be("-ERR Invalid stream ID specified as stream command argument\r\n");
    }

    // ───────────── XREAD ─────────────

    [Fact]
    public async Task Xread_returns_entries_after_id()
    {
        await AddThree();

        (await Run("XREAD", "STREAMS", "s", "1-0")).Should().Be(
            $"*1\r\n*2\r\n{Bulk("s")}" + Entries(Entry("2-0", "b", "2"), Entry("3-0", "c", "3")));
    }

    [Fact]
    public async Task Xread_with_nothing_new_returns_null_array()
    {
        await AddThree();

        (await Run("XREAD", "STREAMS", "s", "3-0")).Should().Be("*-1\r\n");
    }

    [Fact]
    public async Task Xread_with_count_limits_each_stream()
    {
        await AddThree();

        (await Run("XREAD", "COUNT", "1", "STREAMS", "s", "0")).Should().Be(
            $"*1\r\n*2\r\n{Bulk("s")}" + Entries(Entry("1-0", "a", "1")));
    }

    [Fact]
    public async Task Xread_multiple_streams_only_lists_streams_with_data()
    {
        await Run("XADD", "a", "1", "f", "1");
        await Run("XADD", "c", "2", "f", "2");

        (await Run("XREAD", "streams", "a", "b", "c", "0", "0", "0")).Should().Be(
            "*2\r\n" +
            $"*2\r\n{Bulk("a")}" + Entries(Entry("1-0", "f", "1")) +
            $"*2\r\n{Bulk("c")}" + Entries(Entry("2-0", "f", "2")));
    }

    [Fact]
    public async Task Xread_dollar_without_block_returns_null()
    {
        await AddThree();

        (await Run("XREAD", "STREAMS", "s", "$")).Should().Be("*-1\r\n");
    }

    [Theory]
    [InlineData("STREAMS", "a", "b", "0")]
    [InlineData("STREAMS", "a", "b", "c")]
    [InlineData("COUNT", "1", "STREAMS")]
    public async Task Xread_unbalanced_streams_is_an_error(params string[] args)
    {
        (await Run(["XREAD", .. args])).Should().StartWith("-ERR Unbalanced 'xread' list of streams");
    }

    [Fact]
    public async Task Xread_unknown_option_is_a_syntax_error()
    {
        (await Run("XREAD", "LIMIT", "1", "STREAMS", "s", "0")).Should().Be("-ERR syntax error\r\n");
    }

    [Fact]
    public async Task Xread_invalid_id_is_an_error()
    {
        (await Run("XREAD", "STREAMS", "s", "nope")).Should().Be("-ERR Invalid stream ID specified as stream command argument\r\n");
    }

    [Fact]
    public async Task Xread_block_times_out_with_null_array()
    {
        (await Run("XREAD", "BLOCK", "50", "STREAMS", "s", "$")).Should().Be("*-1\r\n");
        H.Get<KeyNotifier>().WatchedKeyCount.Should().Be(0);
    }

    [Fact]
    public async Task Xread_block_wakes_on_xadd()
    {
        await Run("XADD", "s", "1", "old", "x");
        Task<Reply> blocked = H.Dispatch("XREAD", "BLOCK", "0", "STREAMS", "s", "$");
        blocked.IsCompleted.Should().BeFalse();

        await H.Dispatch(H.NewSession(), "XADD", "s", "2", "new", "y");

        (await blocked.WaitAsync(WaitLimit)).ToString().Should().Be(
            $"*1\r\n*2\r\n{Bulk("s")}" + Entries(Entry("2-0", "new", "y")));
    }

    [Fact]
    public async Task Xread_block_with_negative_timeout_is_an_error()
    {
        (await Run("XREAD", "BLOCK", "-1", "STREAMS", "s", "$")).Should().Be("-ERR timeout is negative\r\n");
    }

    [Fact]
    public async Task Xread_block_inside_exec_does_not_block()
    {
        await Run("MULTI");
        await Run("XREAD", "BLOCK", "0", "STREAMS", "s", "$");

        (await Run("EXEC").WaitAsync(WaitLimit)).Should().Be("*1\r\n*-1\r\n");
    }
}
