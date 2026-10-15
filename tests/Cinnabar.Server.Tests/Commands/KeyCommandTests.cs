using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class KeyCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Del_counts_removed_keys()
    {
        await Run("SET", "a", "1");
        await Run("SET", "b", "1");

        (await Run("DEL", "a", "b", "c")).Should().Be(":2\r\n");
        (await Run("DBSIZE")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Del_counts_duplicate_key_once()
    {
        await Run("SET", "a", "1");

        (await Run("DEL", "a", "a")).Should().Be(":1\r\n");
    }

    [Fact]
    public async Task Exists_counts_duplicates_each_time()
    {
        await Run("SET", "a", "1");

        (await Run("EXISTS", "a", "a", "missing")).Should().Be(":2\r\n");
    }

    [Theory]
    [InlineData("SET", "string")]
    [InlineData("RPUSH", "list")]
    [InlineData("XADD", "stream")]
    public async Task Type_reports_value_type(string writer, string expected)
    {
        string[] write = writer switch
        {
            "SET" => ["SET", "k", "v"],
            "RPUSH" => ["RPUSH", "k", "v"],
            _ => ["XADD", "k", "*", "f", "v"],
        };
        await Run(write);

        (await Run("TYPE", "k")).Should().Be($"+{expected}\r\n");
    }

    [Fact]
    public async Task Type_of_missing_key_is_none()
    {
        (await Run("TYPE", "missing")).Should().Be("+none\r\n");
    }

    [Theory]
    [InlineData("*", new[] { "hallo", "hello", "hillo", "world" })]
    [InlineData("h?llo", new[] { "hallo", "hello", "hillo" })]
    [InlineData("h[ae]llo", new[] { "hallo", "hello" })]
    [InlineData("w*", new[] { "world" })]
    [InlineData("nothing*", new string[0])]
    public async Task Keys_matches_glob_patterns(string pattern, string[] expected)
    {
        foreach (string key in new[] { "hello", "hallo", "hillo", "world" })
        {
            await Run("SET", key, "1");
        }

        (await Run("KEYS", pattern)).Should().Be(BulkArray(expected));
    }

    [Fact]
    public async Task Keys_skips_expired_keys()
    {
        await Run("SET", "a", "1", "PX", "10");
        await Run("SET", "b", "1");
        H.Time.Advance(TimeSpan.FromMilliseconds(10));

        (await Run("KEYS", "*")).Should().Be(BulkArray("b"));
    }

    [Fact]
    public async Task DbSize_counts_keys()
    {
        await Run("SET", "a", "1");
        await Run("RPUSH", "b", "1");

        (await Run("DBSIZE")).Should().Be(":2\r\n");
    }

    [Theory]
    [InlineData("FLUSHDB")]
    [InlineData("FLUSHALL")]
    [InlineData("FLUSHDB", "ASYNC")]
    [InlineData("flushall", "sync")]
    public async Task Flush_removes_everything(params string[] command)
    {
        await Run("SET", "a", "1");
        await Run("RPUSH", "b", "1");

        (await Run(command)).Should().Be("+OK\r\n");
        (await Run("DBSIZE")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Flush_with_unknown_option_is_a_syntax_error()
    {
        await Run("SET", "a", "1");

        (await Run("FLUSHDB", "LATER")).Should().Be("-ERR syntax error\r\n");
        (await Run("DBSIZE")).Should().Be(":1\r\n");
    }

    // ───────────── Expiry ─────────────

    [Fact]
    public async Task Expire_on_existing_key_returns_one()
    {
        await Run("SET", "k", "v");

        (await Run("EXPIRE", "k", "10")).Should().Be(":1\r\n");
        (await Run("TTL", "k")).Should().Be(":10\r\n");
    }

    [Theory]
    [InlineData("EXPIRE", "10")]
    [InlineData("PEXPIRE", "10")]
    [InlineData("EXPIREAT", "99999999999")]
    [InlineData("PEXPIREAT", "99999999999")]
    public async Task Expire_family_on_missing_key_returns_zero_and_does_not_propagate(string command, string value)
    {
        Reply reply = await H.Dispatch(command, "missing", value);

        reply.ToString().Should().Be(":0\r\n");
        H.Master.Offset.Should().Be(0);
    }

    [Fact]
    public async Task Expire_with_non_integer_is_an_error()
    {
        await Run("SET", "k", "v");

        (await Run("EXPIRE", "k", "ten")).Should().Be("-ERR value is not an integer or out of range\r\n");
    }

    [Fact]
    public async Task Expire_with_non_positive_ttl_deletes_key()
    {
        await Run("SET", "k", "v");

        (await Run("EXPIRE", "k", "-1")).Should().Be(":1\r\n");
        (await Run("EXISTS", "k")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task PExpire_sets_millisecond_ttl()
    {
        await Run("SET", "k", "v");

        (await Run("PEXPIRE", "k", "1234")).Should().Be(":1\r\n");
        (await Run("PTTL", "k")).Should().Be(":1234\r\n");
    }

    [Fact]
    public async Task ExpireAt_sets_absolute_seconds()
    {
        await Run("SET", "k", "v");
        long at = H.Time.GetUtcNow().ToUnixTimeSeconds() + 50;

        (await Run("EXPIREAT", "k", at.ToString())).Should().Be(":1\r\n");
        (await Run("TTL", "k")).Should().Be(":50\r\n");
    }

    [Fact]
    public async Task PExpireAt_sets_absolute_milliseconds()
    {
        await Run("SET", "k", "v");
        long at = H.NowMs + 750;

        (await Run("PEXPIREAT", "k", at.ToString())).Should().Be(":1\r\n");
        (await Run("PTTL", "k")).Should().Be(":750\r\n");
    }

    [Fact]
    public async Task ExpireAt_in_the_past_deletes_key()
    {
        await Run("SET", "k", "v");

        (await Run("EXPIREAT", "k", "1")).Should().Be(":1\r\n");
        (await Run("EXISTS", "k")).Should().Be(":0\r\n");
    }

    [Theory]
    [InlineData("EXPIRE", "10", 10_000)]
    [InlineData("PEXPIRE", "2500", 2_500)]
    public async Task Relative_expire_propagates_as_absolute_pexpireat(string command, string value, long offsetMs)
    {
        await Run("SET", "k", "v");
        long expected = H.NowMs + offsetMs;

        Reply reply = await H.Dispatch(command, "k", value);

        reply.PropagateAs!.ToString().Should().Be(new Command("PEXPIREAT", "k", expected.ToString()).ToString());
    }

    [Fact]
    public async Task ExpireAt_propagates_as_pexpireat_in_milliseconds()
    {
        await Run("SET", "k", "v");
        long seconds = H.Time.GetUtcNow().ToUnixTimeSeconds() + 60;

        Reply reply = await H.Dispatch("EXPIREAT", "k", seconds.ToString());

        reply.PropagateAs!.ToString().Should().Be(new Command("PEXPIREAT", "k", (seconds * 1000).ToString()).ToString());
    }

    [Theory]
    [InlineData("EXPIRE", "expire")]
    [InlineData("PEXPIRE", "pexpire")]
    public async Task Expire_with_overflowing_value_is_invalid_expire_time(string command, string lower)
    {
        await Run("SET", "k", "v");

        (await Run(command, "k", long.MaxValue.ToString())).Should().Be($"-ERR invalid expire time in '{lower}' command\r\n");
        (await Run("TTL", "k")).Should().Be(":-1\r\n");
    }

    [Fact]
    public async Task Expire_with_very_large_but_representable_value_sets_ttl()
    {
        // 10^12 seconds is about 31,700 years: Redis accepts it (now + 10^15 ms fits in a long).
        await Run("SET", "k", "v");

        (await Run("EXPIRE", "k", "1000000000000")).Should().Be(":1\r\n");
        (await Run("TTL", "k")).Should().Be(":1000000000000\r\n");
    }

    [Fact]
    public async Task Persist_removes_ttl()
    {
        await Run("SET", "k", "v", "EX", "10");

        (await Run("PERSIST", "k")).Should().Be(":1\r\n");
        (await Run("TTL", "k")).Should().Be(":-1\r\n");
    }

    [Fact]
    public async Task Persist_without_ttl_returns_zero_and_does_not_propagate()
    {
        await Run("SET", "k", "v");
        long before = H.Master.Offset;

        (await Run("PERSIST", "k")).Should().Be(":0\r\n");
        (await Run("PERSIST", "missing")).Should().Be(":0\r\n");
        H.Master.Offset.Should().Be(before);
    }

    [Fact]
    public async Task Ttl_of_missing_key_is_minus_two()
    {
        (await Run("TTL", "missing")).Should().Be(":-2\r\n");
        (await Run("PTTL", "missing")).Should().Be(":-2\r\n");
    }

    [Fact]
    public async Task Ttl_without_expiry_is_minus_one()
    {
        await Run("SET", "k", "v");

        (await Run("TTL", "k")).Should().Be(":-1\r\n");
        (await Run("PTTL", "k")).Should().Be(":-1\r\n");
    }

    [Theory]
    [InlineData(1499, 1)]
    [InlineData(1500, 2)]
    [InlineData(400, 0)]
    [InlineData(500, 1)]
    public async Task Ttl_rounds_to_nearest_second(int pttl, int expectedTtl)
    {
        await Run("SET", "k", "v", "PX", pttl.ToString());

        (await Run("TTL", "k")).Should().Be($":{expectedTtl}\r\n");
    }

    [Fact]
    public async Task Ttl_counts_down_with_clock()
    {
        await Run("SET", "k", "v", "EX", "10");

        H.Time.Advance(TimeSpan.FromSeconds(3));

        (await Run("TTL", "k")).Should().Be(":7\r\n");
        (await Run("PTTL", "k")).Should().Be(":7000\r\n");
    }
}
