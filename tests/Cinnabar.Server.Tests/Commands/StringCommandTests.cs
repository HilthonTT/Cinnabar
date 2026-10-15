using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class StringCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Get_missing_key_returns_null()
    {
        (await Run("GET", "missing")).Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Set_then_get_returns_value()
    {
        (await Run("SET", "k", "hello")).Should().Be("+OK\r\n");
        (await Run("GET", "k")).Should().Be("$5\r\nhello\r\n");
    }

    [Fact]
    public async Task Set_replaces_a_list()
    {
        await Run("RPUSH", "k", "a");

        (await Run("SET", "k", "v")).Should().Be("+OK\r\n");
        (await Run("TYPE", "k")).Should().Be("+string\r\n");
    }

    [Fact]
    public async Task Set_nx_writes_only_missing_keys()
    {
        (await Run("SET", "k", "1", "NX")).Should().Be("+OK\r\n");
        (await Run("SET", "k", "2", "nx")).Should().Be("$-1\r\n");
        (await Run("GET", "k")).Should().Be("$1\r\n1\r\n");
    }

    [Fact]
    public async Task Set_xx_writes_only_existing_keys()
    {
        (await Run("SET", "k", "1", "XX")).Should().Be("$-1\r\n");
        await Run("SET", "k", "1");
        (await Run("SET", "k", "2", "XX")).Should().Be("+OK\r\n");
        (await Run("GET", "k")).Should().Be("$1\r\n2\r\n");
    }

    [Theory]
    [InlineData("EX", "10", 10_000)]
    [InlineData("PX", "1500", 1_500)]
    public async Task Set_with_relative_expiry_sets_ttl(string option, string amount, long expectedPttl)
    {
        await Run("SET", "k", "v", option, amount);

        (await Run("PTTL", "k")).Should().Be($":{expectedPttl}\r\n");
    }

    [Fact]
    public async Task Set_with_exat_sets_absolute_expiry()
    {
        long at = H.Time.GetUtcNow().ToUnixTimeSeconds() + 100;

        await Run("SET", "k", "v", "EXAT", at.ToString());

        (await Run("TTL", "k")).Should().Be(":100\r\n");
    }

    [Fact]
    public async Task Set_with_pxat_sets_absolute_expiry()
    {
        long at = H.NowMs + 2_500;

        await Run("SET", "k", "v", "PXAT", at.ToString());

        (await Run("PTTL", "k")).Should().Be(":2500\r\n");
    }

    [Fact]
    public async Task Key_expires_when_clock_passes_ttl()
    {
        await Run("SET", "k", "v", "PX", "100");

        H.Time.Advance(TimeSpan.FromMilliseconds(99));
        (await Run("GET", "k")).Should().Be("$1\r\nv\r\n");

        H.Time.Advance(TimeSpan.FromMilliseconds(1));
        (await Run("GET", "k")).Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Set_without_options_clears_ttl()
    {
        await Run("SET", "k", "v", "EX", "10");

        await Run("SET", "k", "w");

        (await Run("TTL", "k")).Should().Be(":-1\r\n");
    }

    [Fact]
    public async Task Set_keepttl_keeps_existing_ttl()
    {
        await Run("SET", "k", "v", "EX", "10");

        (await Run("SET", "k", "w", "KEEPTTL")).Should().Be("+OK\r\n");

        (await Run("TTL", "k")).Should().Be(":10\r\n");
        (await Run("GET", "k")).Should().Be("$1\r\nw\r\n");
    }

    [Fact]
    public async Task Set_with_past_exat_deletes_key()
    {
        await Run("SET", "k", "v");

        (await Run("SET", "k", "w", "EXAT", "1")).Should().Be("+OK\r\n");

        (await Run("EXISTS", "k")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Set_with_past_exat_propagates_as_del()
    {
        Reply reply = await H.Dispatch("SET", "k", "w", "PXAT", "5");

        reply.PropagateAs!.ToString().Should().Be(new Command("DEL", "k").ToString());
    }

    [Theory]
    [InlineData("EX", "0")]
    [InlineData("EX", "-1")]
    [InlineData("PX", "0")]
    [InlineData("EXAT", "0")]
    [InlineData("PXAT", "-5")]
    public async Task Set_with_non_positive_expire_is_invalid(string option, string amount)
    {
        (await Run("SET", "k", "v", option, amount)).Should().Be("-ERR invalid expire time in 'set' command\r\n");
        (await Run("EXISTS", "k")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Set_with_very_large_but_representable_expire_sets_ttl()
    {
        // Redis accepts any EX whose absolute time fits in a 64-bit millisecond timestamp.
        (await Run("SET", "k", "v", "EX", "1000000000000")).Should().Be("+OK\r\n");

        (await Run("TTL", "k")).Should().Be(":1000000000000\r\n");
    }

    [Fact]
    public async Task Set_with_non_numeric_expire_is_not_an_integer()
    {
        (await Run("SET", "k", "v", "EX", "soon")).Should().Be("-ERR value is not an integer or out of range\r\n");
    }

    [Theory]
    [InlineData("NX", "XX")]
    [InlineData("XX", "NX")]
    [InlineData("EX", "10", "PX", "100")]
    [InlineData("EX", "10", "KEEPTTL")]
    [InlineData("KEEPTTL", "EX", "10")]
    [InlineData("EX")]
    [InlineData("BOGUS")]
    public async Task Set_with_conflicting_or_unknown_options_is_a_syntax_error(params string[] options)
    {
        (await Run(["SET", "k", "v", .. options])).Should().Be("-ERR syntax error\r\n");
    }

    [Fact]
    public async Task Set_with_expiry_propagates_as_absolute_pxat()
    {
        long expected = H.NowMs + 10_000;

        Reply reply = await H.Dispatch("SET", "k", "v", "EX", "10");

        reply.PropagateAs!.ToString().Should().Be(new Command("SET", "k", "v", "PXAT", expected.ToString()).ToString());
    }

    [Fact]
    public async Task Set_with_keepttl_propagates_keepttl()
    {
        Reply reply = await H.Dispatch("SET", "k", "v", "KEEPTTL");

        reply.PropagateAs!.ToString().Should().Be(new Command("SET", "k", "v", "KEEPTTL").ToString());
    }

    [Fact]
    public async Task Plain_set_propagates_without_options()
    {
        Reply reply = await H.Dispatch("SET", "k", "v", "NX");

        reply.PropagateAs!.ToString().Should().Be(new Command("SET", "k", "v").ToString());
    }

    // ───────────── INCR family ─────────────

    [Fact]
    public async Task Incr_creates_missing_key()
    {
        (await Run("INCR", "n")).Should().Be(":1\r\n");
        (await Run("INCR", "n")).Should().Be(":2\r\n");
    }

    [Fact]
    public async Task Decr_goes_negative()
    {
        (await Run("DECR", "n")).Should().Be(":-1\r\n");
    }

    [Fact]
    public async Task IncrBy_and_DecrBy_apply_amount()
    {
        (await Run("INCRBY", "n", "10")).Should().Be(":10\r\n");
        (await Run("DECRBY", "n", "3")).Should().Be(":7\r\n");
        (await Run("INCRBY", "n", "-7")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Incr_on_non_integer_is_an_error()
    {
        await Run("SET", "n", "abc");

        (await Run("INCR", "n")).Should().Be("-ERR value is not an integer or out of range\r\n");
    }

    [Fact]
    public async Task IncrBy_with_non_integer_amount_is_an_error()
    {
        (await Run("INCRBY", "n", "1.5")).Should().Be("-ERR value is not an integer or out of range\r\n");
    }

    [Fact]
    public async Task Incr_overflow_is_an_error()
    {
        await Run("SET", "n", long.MaxValue.ToString());

        (await Run("INCR", "n")).Should().Be("-ERR increment or decrement would overflow\r\n");
    }

    [Fact]
    public async Task DecrBy_with_long_min_value_is_rejected()
    {
        (await Run("DECRBY", "n", long.MinValue.ToString())).Should().Be("-ERR decrement would overflow\r\n");
        (await Run("EXISTS", "n")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Incr_on_list_is_wrongtype()
    {
        await Run("RPUSH", "l", "a");

        (await Run("INCR", "l")).Should().StartWith("-WRONGTYPE");
    }

    [Fact]
    public async Task Incr_preserves_ttl()
    {
        await Run("SET", "n", "1", "EX", "100");

        await Run("INCR", "n");

        (await Run("TTL", "n")).Should().Be(":100\r\n");
    }
}
