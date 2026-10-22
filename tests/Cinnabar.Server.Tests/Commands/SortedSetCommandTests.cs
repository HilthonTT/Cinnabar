using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class SortedSetCommandTests : DispatcherTestBase
{
    private Task Seed() => Run("ZADD", "z", "1", "a", "2", "b", "3", "c", "4", "d");

    [Fact]
    public async Task Zadd_counts_new_members_and_ch_counts_changes()
    {
        (await Run("ZADD", "z", "1", "a", "2", "b")).Should().Be(":2\r\n");
        (await Run("ZADD", "z", "5", "a", "2", "b", "3", "c")).Should().Be(":1\r\n");
        (await Run("ZADD", "z", "CH", "6", "a", "2", "b")).Should().Be(":1\r\n");
        (await Run("TYPE", "z")).Should().Be("+zset\r\n");
    }

    [Fact]
    public async Task Zadd_incr_returns_score_or_nil()
    {
        (await Run("ZADD", "z", "INCR", "1.5", "a")).Should().Be(Bulk("1.5"));
        (await Run("ZADD", "z", "INCR", "1", "a")).Should().Be(Bulk("2.5"));
        (await Run("ZADD", "z", "NX", "INCR", "1", "a")).Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Zadd_gt_lt_nx_xx()
    {
        await Run("ZADD", "z", "5", "a");

        await Run("ZADD", "z", "GT", "3", "a");
        (await Run("ZSCORE", "z", "a")).Should().Be(Bulk("5"));
        await Run("ZADD", "z", "LT", "3", "a");
        (await Run("ZSCORE", "z", "a")).Should().Be(Bulk("3"));
        await Run("ZADD", "z", "XX", "1", "new");
        (await Run("ZSCORE", "z", "new")).Should().Be("$-1\r\n");
    }

    [Theory]
    [InlineData("-ERR XX and NX options at the same time are not compatible\r\n", "NX", "XX", "1", "a")]
    [InlineData("-ERR GT, LT, and/or NX options at the same time are not compatible\r\n", "GT", "LT", "1", "a")]
    [InlineData("-ERR GT, LT, and/or NX options at the same time are not compatible\r\n", "NX", "GT", "1", "a")]
    [InlineData("-ERR INCR option supports a single increment-element pair\r\n", "INCR", "1", "a", "2", "b")]
    [InlineData("-ERR syntax error\r\n", "1", "a", "2")]
    [InlineData("-ERR value is not a valid float\r\n", "abc", "a")]
    [InlineData("-ERR value is not a valid float\r\n", "nan", "a")]
    public async Task Zadd_validates_arguments(string expected, params string[] args)
    {
        (await Run(["ZADD", "z", .. args])).Should().Be(expected);
        H.Keyspace.Exists("z").Should().BeFalse();
    }

    [Fact]
    public async Task Scores_are_formatted_like_redis()
    {
        await Run("ZADD", "z", "1", "one", "-0.5", "half", "1e20", "big", "+inf", "top", "-inf", "bottom");

        (await Run("ZRANGE", "z", "0", "-1", "WITHSCORES")).Should().Be(BulkArray(
            "bottom", "-inf", "half", "-0.5", "one", "1", "big", "1e+20", "top", "inf"));
    }

    [Fact]
    public async Task Zrange_by_rank_and_reverse()
    {
        await Seed();

        (await Run("ZRANGE", "z", "0", "1")).Should().Be(BulkArray("a", "b"));
        (await Run("ZRANGE", "z", "-2", "-1", "WITHSCORES")).Should().Be(BulkArray("c", "3", "d", "4"));
        (await Run("ZRANGE", "z", "0", "0", "REV")).Should().Be(BulkArray("d"));
        (await Run("ZREVRANGE", "z", "0", "1")).Should().Be(BulkArray("d", "c"));
    }

    [Fact]
    public async Task Zrange_byscore_with_limit_and_rev()
    {
        await Seed();

        (await Run("ZRANGE", "z", "(1", "3", "BYSCORE")).Should().Be(BulkArray("b", "c"));
        (await Run("ZRANGE", "z", "+inf", "-inf", "BYSCORE", "REV", "LIMIT", "1", "2")).Should().Be(BulkArray("c", "b"));
        (await Run("ZRANGEBYSCORE", "z", "-inf", "+inf", "WITHSCORES", "LIMIT", "0", "1")).Should().Be(BulkArray("a", "1"));
        (await Run("ZREVRANGEBYSCORE", "z", "4", "(2")).Should().Be(BulkArray("d", "c"));
    }

    [Fact]
    public async Task Zrange_bylex()
    {
        await Run("ZADD", "z", "0", "a", "0", "b", "0", "c");

        (await Run("ZRANGE", "z", "[b", "+", "BYLEX")).Should().Be(BulkArray("b", "c"));
        (await Run("ZRANGEBYLEX", "z", "-", "(c")).Should().Be(BulkArray("a", "b"));
        (await Run("ZREVRANGEBYLEX", "z", "+", "-", "LIMIT", "0", "1")).Should().Be(BulkArray("c"));
        (await Run("ZLEXCOUNT", "z", "[a", "[b")).Should().Be(":2\r\n");
    }

    [Theory]
    [InlineData("-ERR syntax error, LIMIT is only supported in combination with either BYSCORE or BYLEX\r\n", "0", "1", "LIMIT", "0", "1")]
    [InlineData("-ERR syntax error, WITHSCORES not supported in combination with BYLEX\r\n", "-", "+", "BYLEX", "WITHSCORES")]
    [InlineData("-ERR min or max is not a float\r\n", "x", "1", "BYSCORE")]
    [InlineData("-ERR min or max not valid string range item\r\n", "a", "+", "BYLEX")]
    [InlineData("-ERR syntax error\r\n", "0", "1", "BOGUS")]
    [InlineData("-ERR value is not an integer or out of range\r\n", "x", "1")]
    public async Task Zrange_validates_arguments(string expected, params string[] args)
    {
        await Seed();

        (await Run(["ZRANGE", "z", .. args])).Should().Be(expected);
    }

    [Fact]
    public async Task Zscore_zmscore_zcard_zcount()
    {
        await Seed();

        (await Run("ZSCORE", "z", "b")).Should().Be(Bulk("2"));
        (await Run("ZMSCORE", "z", "a", "zz")).Should().Be(BulkArray("1", null));
        (await Run("ZCARD", "z")).Should().Be(":4\r\n");
        (await Run("ZCOUNT", "z", "(1", "+inf")).Should().Be(":3\r\n");
    }

    [Fact]
    public async Task Zrank_and_zrevrank()
    {
        await Seed();

        (await Run("ZRANK", "z", "c")).Should().Be(":2\r\n");
        (await Run("ZREVRANK", "z", "c")).Should().Be(":1\r\n");
        (await Run("ZRANK", "z", "c", "WITHSCORE")).Should().Be("*2\r\n:2\r\n" + Bulk("3"));
        (await Run("ZRANK", "z", "zz")).Should().Be("$-1\r\n");
        (await Run("ZRANK", "z", "zz", "WITHSCORE")).Should().Be("*-1\r\n");
    }

    [Fact]
    public async Task Zincrby_and_zrem()
    {
        (await Run("ZINCRBY", "z", "2", "a")).Should().Be(Bulk("2"));
        (await Run("ZINCRBY", "z", "-0.5", "a")).Should().Be(Bulk("1.5"));
        (await Run("ZREM", "z", "a", "zz")).Should().Be(":1\r\n");
        (await Run("EXISTS", "z")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Zpopmin_and_zpopmax()
    {
        await Seed();

        (await Run("ZPOPMIN", "z")).Should().Be(BulkArray("a", "1"));
        (await Run("ZPOPMAX", "z", "2")).Should().Be(BulkArray("d", "4", "c", "3"));
        (await Run("ZPOPMIN", "missing")).Should().Be("*0\r\n");
    }

    [Fact]
    public async Task Zremrange_variants()
    {
        await Seed();

        (await Run("ZREMRANGEBYRANK", "z", "0", "0")).Should().Be(":1\r\n");
        (await Run("ZREMRANGEBYSCORE", "z", "4", "+inf")).Should().Be(":1\r\n");
        (await Run("ZREMRANGEBYLEX", "z", "-", "+")).Should().Be(":2\r\n");
        (await Run("EXISTS", "z")).Should().Be(":0\r\n");
    }


    [Fact]
    public async Task Sorted_set_commands_on_a_hash_are_wrongtype()
    {
        await Run("HSET", "h", "f", "v");

        (await Run("ZADD", "h", "1", "a")).Should().StartWith("-WRONGTYPE");
        (await Run("ZRANGE", "h", "0", "-1")).Should().StartWith("-WRONGTYPE");
    }
}
