using Cinnabar.Server.Configuration;
using Cinnabar.Storage;

namespace Cinnabar.Server.Tests.Configuration;

public sealed class MemorySettingsTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1048576", 1048576)]
    [InlineData("100mb", 100L << 20)]
    [InlineData("2GB", 2L << 30)]
    [InlineData("512kb", 512L << 10)]
    [InlineData("1k", 1000)]
    [InlineData("3m", 3_000_000)]
    public void Sizes_parse_with_redis_units(string text, long bytes)
    {
        MemorySettings.ParseSize(text).Should().Be(bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("ten")]
    [InlineData("10tb")]
    [InlineData("99999999999gb")]
    public void Bad_sizes_throw(string text)
    {
        FluentActions.Invoking(() => MemorySettings.ParseSize(text)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Flags_set_the_memory_settings()
    {
        ServerSettings settings = CommandLine.Parse(
            ["--maxmemory", "64mb", "--maxmemory-policy", "VOLATILE-TTL", "--maxmemory-samples", "10"], out _);

        settings.Memory.MaxMemoryBytes.Should().Be(64L << 20);
        settings.Memory.Policy.Should().Be(EvictionPolicy.VolatileTtl);
        settings.Memory.MaxMemorySamples.Should().Be(10);
    }

    [Theory]
    [InlineData("--maxmemory", "lots")]
    [InlineData("--maxmemory-policy", "allkeys-lfu")]
    [InlineData("--maxmemory-samples", "0")]
    public void Invalid_flags_fail_at_parse_time(string flag, string value)
    {
        FluentActions.Invoking(() => CommandLine.Parse([flag, value], out _)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_policy_name_round_trips()
    {
        foreach (string name in EvictionPolicyNames.All)
        {
            EvictionPolicyNames.TryParse(name, out EvictionPolicy? policy).Should().BeTrue();
            EvictionPolicyNames.ToName(policy!.Value).Should().Be(name);
        }
    }
}
