using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

/// <summary>Regression tests for bugs found in an audit of the command layer.</summary>
public sealed class RegressionCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Xread_count_zero_means_no_limit()
    {
        await Run("XADD", "s", "1-0", "f", "v");
        await Run("XADD", "s", "2-0", "f", "v");

        string reply = await Run("XREAD", "COUNT", "0", "STREAMS", "s", "0");

        reply.Should().Contain("1-0").And.Contain("2-0");
    }

    [Fact]
    public async Task Xread_negative_count_means_no_limit()
    {
        await Run("XADD", "s", "1-0", "f", "v");

        (await Run("XREAD", "COUNT", "-1", "STREAMS", "s", "0")).Should().Contain("1-0");
    }

    [Fact]
    public async Task Xreadgroup_negative_count_means_no_limit()
    {
        await Run("XADD", "s", "1-0", "f", "v");
        await Run("XGROUP", "CREATE", "s", "g", "0");

        (await Run("XREADGROUP", "GROUP", "g", "c", "COUNT", "-1", "STREAMS", "s", ">")).Should().Contain("1-0");
    }

    [Theory]
    [InlineData("+5")]
    [InlineData("05")]
    [InlineData("-0")]
    public async Task Integer_arguments_are_parsed_strictly(string delta)
    {
        (await Run("INCRBY", "k", delta)).Should().Be("-ERR value is not an integer or out of range\r\n");
    }

    [Fact]
    public void Timeouts_beyond_the_timer_limit_block_forever()
    {
        Arguments.ParseTimeoutSeconds("5000000").Should().BeNull();
        Arguments.ParseTimeoutSeconds("1e20").Should().BeNull();
        Arguments.ParseTimeoutMilliseconds("5000000000").Should().BeNull();
    }

    [Fact]
    public void Sub_millisecond_timeouts_block_forever_like_redis()
    {
        Arguments.ParseTimeoutSeconds("0.0001").Should().BeNull();
        Arguments.ParseTimeoutSeconds("0.5").Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task Blpop_with_a_huge_timeout_is_not_an_error()
    {
        await Run("RPUSH", "k", "v");

        (await Run("BLPOP", "k", "5000000")).Should().Be(BulkArray("k", "v"));
    }

    [Fact]
    public async Task Commands_after_subscribe_inside_exec_still_run()
    {
        await Run("SET", "k", "v");
        await Run("MULTI");
        await Run("SUBSCRIBE", "c");
        await Run("GET", "k");

        string reply = await Run("EXEC");

        reply.Should().Contain(Bulk("v")).And.NotContain("Can't execute");
    }
}

public sealed class ReplicaRegressionCommandTests : DispatcherTestBase
{
    public ReplicaRegressionCommandTests()
        : base(s => s.Replication.MasterHost = "127.0.0.1")
    {
    }

    [Fact]
    public async Task Write_queued_on_a_replica_aborts_the_transaction()
    {
        await Run("MULTI");
        (await Run("GET", "a")).Should().Be("+QUEUED\r\n");
        (await Run("SET", "a", "1")).Should().StartWith("-READONLY");

        (await Run("EXEC")).Should().StartWith("-EXECABORT");
    }
}
