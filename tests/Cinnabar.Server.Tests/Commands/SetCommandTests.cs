using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class SetCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Sadd_srem_scard_sismember()
    {
        (await Run("SADD", "s", "a", "b", "a")).Should().Be(":2\r\n");
        (await Run("SCARD", "s")).Should().Be(":2\r\n");
        (await Run("SISMEMBER", "s", "a")).Should().Be(":1\r\n");
        (await Run("SISMEMBER", "s", "zz")).Should().Be(":0\r\n");
        (await Run("SMISMEMBER", "s", "a", "zz", "b")).Should().Be("*3\r\n:1\r\n:0\r\n:1\r\n");
        (await Run("SREM", "s", "a", "zz")).Should().Be(":1\r\n");
        (await Run("TYPE", "s")).Should().Be("+set\r\n");
    }

    [Fact]
    public async Task Smembers_returns_every_member()
    {
        await Run("SADD", "s", "x", "y");

        string reply = await Run("SMEMBERS", "s");

        reply.Should().StartWith("*2\r\n").And.Contain(Bulk("x")).And.Contain(Bulk("y"));
        (await Run("SMEMBERS", "missing")).Should().Be("*0\r\n");
    }

    [Fact]
    public async Task Spop_without_count_returns_one_member_or_nil()
    {
        await Run("SADD", "s", "only");

        (await Run("SPOP", "s")).Should().Be(Bulk("only"));
        (await Run("SPOP", "s")).Should().Be("$-1\r\n");
        (await Run("SPOP", "s", "3")).Should().Be("*0\r\n");
    }

    [Fact]
    public async Task Spop_propagates_the_members_it_removed()
    {
        await Run("SADD", "s", "a", "b", "c");

        Reply reply = await H.Dispatch("SPOP", "s", "2");

        reply.PropagateAs!.Name.Should().Be("SREM");
        reply.PropagateAs.Arguments[0].Should().Be("s");
        reply.PropagateAs.Arguments.Skip(1).Should().HaveCount(2);
        H.Keyspace.SetCardinality("s").Should().Be(1);
    }

    [Fact]
    public async Task Spop_rejects_negative_count()
    {
        (await Run("SPOP", "s", "-1")).Should().Be("-ERR value is out of range, must be positive\r\n");
    }

    [Fact]
    public async Task Srandmember_does_not_remove()
    {
        await Run("SADD", "s", "a", "b");

        (await Run("SRANDMEMBER", "s")).Should().BeOneOf(Bulk("a"), Bulk("b"));
        (await Run("SRANDMEMBER", "s", "-4")).Should().StartWith("*4\r\n");
        (await Run("SRANDMEMBER", "s", "10")).Should().StartWith("*2\r\n");
        (await Run("SRANDMEMBER", "missing")).Should().Be("$-1\r\n");
        (await Run("SCARD", "s")).Should().Be(":2\r\n");
    }

    [Fact]
    public async Task Smove_moves_between_sets()
    {
        await Run("SADD", "a", "m");

        (await Run("SMOVE", "a", "b", "m")).Should().Be(":1\r\n");
        (await Run("SMOVE", "a", "b", "m")).Should().Be(":0\r\n");
        (await Run("SMEMBERS", "b")).Should().Be(BulkArray("m"));
    }

    [Fact]
    public async Task Sinter_sunion_sdiff()
    {
        await Run("SADD", "a", "1", "2", "3");
        await Run("SADD", "b", "2", "3", "4");

        (await Run("SINTER", "a", "b")).Should().StartWith("*2\r\n").And.Contain(Bulk("2")).And.Contain(Bulk("3"));
        (await Run("SUNION", "a", "b")).Should().StartWith("*4\r\n");
        (await Run("SDIFF", "a", "b")).Should().Be(BulkArray("1"));
    }

    [Fact]
    public async Task Store_variants_write_the_destination()
    {
        await Run("SADD", "a", "1", "2");
        await Run("SADD", "b", "2", "3");

        (await Run("SINTERSTORE", "i", "a", "b")).Should().Be(":1\r\n");
        (await Run("SUNIONSTORE", "u", "a", "b")).Should().Be(":3\r\n");
        (await Run("SDIFFSTORE", "d", "a", "b")).Should().Be(":1\r\n");
        (await Run("SMEMBERS", "i")).Should().Be(BulkArray("2"));
        (await Run("SMEMBERS", "d")).Should().Be(BulkArray("1"));
    }

    [Fact]
    public async Task Sintercard_counts_with_limit()
    {
        await Run("SADD", "a", "1", "2", "3");
        await Run("SADD", "b", "1", "2", "3");

        (await Run("SINTERCARD", "2", "a", "b")).Should().Be(":3\r\n");
        (await Run("SINTERCARD", "2", "a", "b", "LIMIT", "2")).Should().Be(":2\r\n");
    }

    [Theory]
    [InlineData("-ERR numkeys should be greater than 0\r\n", "0", "a")]
    [InlineData("-ERR Number of keys can't be greater than number of args\r\n", "3", "a", "b")]
    [InlineData("-ERR syntax error\r\n", "1", "a", "b")]
    [InlineData("-ERR LIMIT can't be negative\r\n", "1", "a", "LIMIT", "-1")]
    public async Task Sintercard_validates_arguments(string expected, params string[] args)
    {
        (await Run(["SINTERCARD", .. args])).Should().Be(expected);
    }


    [Fact]
    public async Task Set_commands_on_a_list_are_wrongtype()
    {
        await Run("RPUSH", "l", "a");

        (await Run("SADD", "l", "x")).Should().StartWith("-WRONGTYPE");
        (await Run("SINTER", "l")).Should().StartWith("-WRONGTYPE");
    }
}
