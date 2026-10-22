using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class HashCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Hset_returns_new_field_count_and_hget_reads()
    {
        (await Run("HSET", "h", "a", "1", "b", "2")).Should().Be(":2\r\n");
        (await Run("HSET", "h", "a", "9", "c", "3")).Should().Be(":1\r\n");

        (await Run("HGET", "h", "a")).Should().Be(Bulk("9"));
        (await Run("HGET", "h", "zz")).Should().Be("$-1\r\n");
        (await Run("TYPE", "h")).Should().Be("+hash\r\n");
    }

    [Theory]
    [InlineData("HSET", "h", "f")]
    [InlineData("HSET", "h", "f", "v", "g")]
    [InlineData("HMSET", "h", "f", "v", "g")]
    public async Task Hset_needs_field_value_pairs(params string[] parts)
    {
        (await Run(parts)).Should().StartWith("-ERR wrong number of arguments for");
    }

    [Fact]
    public async Task Hmset_replies_ok()
    {
        (await Run("HMSET", "h", "a", "1")).Should().Be("+OK\r\n");
        (await Run("HMGET", "h", "a", "zz")).Should().Be(BulkArray("1", null));
    }

    [Fact]
    public async Task Hsetnx_only_sets_missing_fields()
    {
        (await Run("HSETNX", "h", "f", "1")).Should().Be(":1\r\n");
        (await Run("HSETNX", "h", "f", "2")).Should().Be(":0\r\n");
        (await Run("HGET", "h", "f")).Should().Be(Bulk("1"));
    }

    [Fact]
    public async Task Hgetall_hkeys_hvals_and_hlen()
    {
        await Run("HSET", "h", "a", "1", "b", "2");

        string all = await Run("HGETALL", "h");
        all.Should().StartWith("*4\r\n").And.Contain(Bulk("a") + Bulk("1")).And.Contain(Bulk("b") + Bulk("2"));
        (await Run("HKEYS", "h")).Should().StartWith("*2\r\n").And.Contain(Bulk("a")).And.Contain(Bulk("b"));
        (await Run("HVALS", "h")).Should().StartWith("*2\r\n").And.Contain(Bulk("1")).And.Contain(Bulk("2"));
        (await Run("HLEN", "h")).Should().Be(":2\r\n");
        (await Run("HGETALL", "missing")).Should().Be("*0\r\n");
    }

    [Fact]
    public async Task Hdel_hexists_hstrlen()
    {
        await Run("HSET", "h", "a", "héllo", "b", "2");

        (await Run("HSTRLEN", "h", "a")).Should().Be(":6\r\n");
        (await Run("HEXISTS", "h", "a")).Should().Be(":1\r\n");
        (await Run("HDEL", "h", "a", "zz")).Should().Be(":1\r\n");
        (await Run("HEXISTS", "h", "a")).Should().Be(":0\r\n");
        (await Run("HDEL", "h", "b")).Should().Be(":1\r\n");
        (await Run("EXISTS", "h")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Hincrby_and_errors()
    {
        (await Run("HINCRBY", "h", "n", "5")).Should().Be(":5\r\n");
        (await Run("HINCRBY", "h", "n", "-2")).Should().Be(":3\r\n");
        (await Run("HINCRBY", "h", "n", "x")).Should().Be("-ERR value is not an integer or out of range\r\n");

        await Run("HSET", "h", "s", "text");
        (await Run("HINCRBY", "h", "s", "1")).Should().Be("-ERR hash value is not an integer\r\n");
    }

    [Fact]
    public async Task Hincrbyfloat_returns_and_propagates_the_result()
    {
        (await Run("HINCRBYFLOAT", "h", "f", "10.5")).Should().Be(Bulk("10.5"));

        Reply reply = await H.Dispatch("HINCRBYFLOAT", "h", "f", "0.1");

        reply.ToString().Should().Be(Bulk("10.6"));
        reply.PropagateAs!.ToString().Should().Be("HSET h f 10.6");
    }

    [Theory]
    [InlineData("abc", "-ERR value is not a valid float\r\n")]
    [InlineData("inf", "-ERR increment would produce NaN or Infinity\r\n")]
    public async Task Hincrbyfloat_rejects_bad_increments(string increment, string expected)
    {
        (await Run("HINCRBYFLOAT", "h", "f", increment)).Should().Be(expected);
    }

    [Fact]
    public async Task Hash_commands_on_a_string_are_wrongtype()
    {
        await Run("SET", "s", "v");

        (await Run("HGET", "s", "f")).Should().StartWith("-WRONGTYPE");
        (await Run("HSET", "s", "f", "v")).Should().StartWith("-WRONGTYPE");
    }


    [Fact]
    public async Task No_op_writes_are_not_propagated()
    {
        await Run("HSET", "h", "f", "v");
        long before = H.Master.Offset;

        await Run("HDEL", "h", "zz");
        await Run("HSETNX", "h", "f", "other");

        H.Master.Offset.Should().Be(before);
    }
}
