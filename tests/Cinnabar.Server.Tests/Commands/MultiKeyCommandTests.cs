using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

/// <summary>MGET, MSET, MSETNX, STRLEN and SCAN.</summary>
public sealed class MultiKeyCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Mset_then_mget()
    {
        (await Run("MSET", "a", "1", "b", "2")).Should().Be("+OK\r\n");

        (await Run("MGET", "a", "missing", "b")).Should().Be(BulkArray("1", null, "2"));
    }

    [Fact]
    public async Task Mget_returns_nil_for_other_types()
    {
        await Run("RPUSH", "l", "x");

        (await Run("MGET", "l")).Should().Be(BulkArray([null]));
    }

    [Fact]
    public async Task Mset_needs_pairs()
    {
        (await Run("MSET", "a", "1", "b")).Should().Be("-ERR wrong number of arguments for 'mset' command\r\n");
        (await Run("MSETNX", "a")).Should().Be("-ERR wrong number of arguments for 'msetnx' command\r\n");
    }

    [Fact]
    public async Task Msetnx_is_all_or_nothing()
    {
        await Run("SET", "b", "old");

        (await Run("MSETNX", "a", "1", "b", "2")).Should().Be(":0\r\n");
        (await Run("EXISTS", "a")).Should().Be(":0\r\n");
        (await Run("MSETNX", "a", "1", "c", "3")).Should().Be(":1\r\n");
    }

    [Fact]
    public async Task Strlen_counts_bytes()
    {
        await Run("SET", "k", "héllo");

        (await Run("STRLEN", "k")).Should().Be(":6\r\n");
        (await Run("STRLEN", "missing")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Scan_iterates_every_key_exactly_once()
    {
        for (int i = 0; i < 40; i++)
        {
            await Run("SET", $"k{i}", "v");
        }

        var seen = new List<string>();
        string cursor = "0";
        do
        {
            IReadOnlyList<Protocol.RespValue> reply = Parse(await Run("SCAN", cursor, "COUNT", "7"));
            cursor = reply[0].Text!;
            seen.AddRange(reply[1].AsStringList()!);
        }
        while (cursor != "0");

        seen.Should().HaveCount(40).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Scan_filters_by_match_and_type()
    {
        await Run("SET", "user:1", "v");
        await Run("HSET", "user:2", "f", "v");
        await Run("SET", "other", "v");

        (await Run("SCAN", "0", "MATCH", "user:*", "TYPE", "hash", "COUNT", "100"))
            .Should().Be("*2\r\n" + Bulk("0") + BulkArray("user:2"));
    }

    [Theory]
    [InlineData("-ERR invalid cursor\r\n", "abc")]
    [InlineData("-ERR invalid cursor\r\n", "-1")]
    [InlineData("-ERR syntax error\r\n", "0", "COUNT", "0")]
    [InlineData("-ERR syntax error\r\n", "0", "MATCH")]
    [InlineData("-ERR syntax error\r\n", "0", "BOGUS", "x")]
    [InlineData("-ERR unknown type name\r\n", "0", "TYPE", "widget")]
    public async Task Scan_validates_arguments(string expected, params string[] args)
    {
        (await Run(["SCAN", .. args])).Should().Be(expected);
    }

    [Fact]
    public async Task Type_reports_redis_names()
    {
        await Run("SET", "s", "v");
        await Run("RPUSH", "l", "v");
        await Run("XADD", "x", "*", "f", "v");
        await Run("HSET", "h", "f", "v");
        await Run("SADD", "st", "v");
        await Run("ZADD", "z", "1", "v");

        (await Run("TYPE", "s")).Should().Be("+string\r\n");
        (await Run("TYPE", "l")).Should().Be("+list\r\n");
        (await Run("TYPE", "x")).Should().Be("+stream\r\n");
        (await Run("TYPE", "h")).Should().Be("+hash\r\n");
        (await Run("TYPE", "st")).Should().Be("+set\r\n");
        (await Run("TYPE", "z")).Should().Be("+zset\r\n");
        (await Run("TYPE", "missing")).Should().Be("+none\r\n");
    }

    private static IReadOnlyList<Protocol.RespValue> Parse(string wire)
    {
        var buffer = new System.Buffers.ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(wire));
        Protocol.RespReader.TryRead(ref buffer, out Protocol.RespValue? value).Should().BeTrue();
        return value!.AsArray();
    }
}
