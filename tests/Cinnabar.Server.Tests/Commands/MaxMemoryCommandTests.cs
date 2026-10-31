using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class MaxMemoryNoEvictionTests() : DispatcherTestBase(s => s.Memory.MaxMemory = "4kb")
{
    private const string Oom = "-OOM command not allowed when used memory > 'maxmemory'.\r\n";

    [Fact]
    public async Task Writes_are_refused_over_the_limit_but_deletes_work()
    {
        await Run("SET", "big", new string('x', 4096));

        (await Run("SET", "more", "v")).Should().Be(Oom);
        (await Run("RPUSH", "l", "v")).Should().Be(Oom);
        (await Run("GET", "big")).Should().StartWith("$4096");

        (await Run("DEL", "big")).Should().Be(":1\r\n");
        (await Run("SET", "more", "v")).Should().Be("+OK\r\n");
    }

    [Fact]
    public async Task Refused_writes_are_not_replicated()
    {
        await Run("SET", "big", new string('x', 4096));
        long before = H.Master.Offset;

        await Run("SET", "more", "v");

        H.Master.Offset.Should().Be(before);
    }

    [Fact]
    public async Task Config_get_reports_the_limit()
    {
        (await Run("CONFIG", "GET", "maxmemory*")).Should().Be(
            BulkArray("maxmemory", "4096", "maxmemory-policy", "noeviction", "maxmemory-samples", "5"));
    }

    [Fact]
    public async Task Info_reports_memory()
    {
        await Run("SET", "k", "v");

        string info = (await H.Dispatch("INFO", "memory")).ToString();

        info.Should().Contain("maxmemory:4096").And.Contain("maxmemory_policy:noeviction");
        info.Should().MatchRegex(@"used_memory:[1-9]\d*");
    }
}

public sealed class MaxMemoryEvictionTests() : DispatcherTestBase(s =>
{
    s.Memory.MaxMemory = "16kb";
    s.Memory.MaxMemoryPolicy = "allkeys-lru";
})
{
    [Fact]
    public async Task Writes_evict_keys_to_stay_under_the_limit()
    {
        for (int i = 0; i < 100; i++)
        {
            (await Run("SET", $"k{i}", new string('x', 500))).Should().Be("+OK\r\n");
        }

        H.Keyspace.EvictedKeys.Should().BeGreaterThan(0);
        H.Keyspace.Count.Should().BeLessThan(100);

        // The check runs before each write, so the last write can overshoot by its own size.
        H.Keyspace.MemoryUsage.Should().BeLessThan((16 * 1024) + 2000);
        (await Run("GET", "k99")).Should().StartWith("$500");
    }

    [Fact]
    public async Task Evictions_are_replicated_as_del()
    {
        await Run("SET", "victim", new string('x', 20_000));
        long before = H.Master.Offset;

        await Run("SET", "small", "v");

        long expected = new Command("DEL", "victim").Encode().Length + new Command("SET", "small", "v").Encode().Length;
        H.Master.Offset.Should().Be(before + expected);
        (await Run("EXISTS", "victim")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Info_reports_evicted_keys()
    {
        await Run("SET", "victim", new string('x', 20_000));
        await Run("SET", "small", "v");

        (await H.Dispatch("INFO", "stats")).ToString().Should().Contain("evicted_keys:1");
    }
}
