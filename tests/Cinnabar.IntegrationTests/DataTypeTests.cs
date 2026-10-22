using Cinnabar.Client;

namespace Cinnabar.IntegrationTests;

/// <summary>Hashes, sets and sorted sets through the client library.</summary>
public sealed class DataTypeTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Hashes_RoundTripThroughTheClient()
    {
        (await Client.HSetAsync("user:1", [new("name", "Ada"), new("city", "Zürich")])).Should().Be(2);
        (await Client.HSetAsync("user:1", "name", "Ada L.")).Should().BeFalse();

        (await Client.HGetAsync("user:1", "city")).Should().Be("Zürich");
        (await Client.HGetAllAsync("user:1")).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["name"] = "Ada L.",
            ["city"] = "Zürich",
        });
        (await Client.HMGetAsync("user:1", "name", "missing")).Should().Equal("Ada L.", null);
        (await Client.HIncrByAsync("user:1", "visits", 3)).Should().Be(3);
        (await Client.HIncrByFloatAsync("user:1", "balance", 2.5)).Should().Be(2.5);
        (await Client.HLenAsync("user:1")).Should().Be(4);
        (await Client.HDelAsync("user:1", "visits", "balance")).Should().Be(2);
        (await Client.HExistsAsync("user:1", "visits")).Should().BeFalse();
        (await Client.TypeAsync("user:1")).Should().Be("hash");
    }

    [Fact]
    public async Task Sets_RoundTripThroughTheClient()
    {
        (await Client.SAddAsync("a", "1", "2", "3")).Should().Be(3);
        await Client.SAddAsync("b", "3", "4");

        (await Client.SIsMemberAsync("a", "2")).Should().BeTrue();
        (await Client.SInterAsync("a", "b")).Should().Equal("3");
        (await Client.SUnionAsync("a", "b")).Should().BeEquivalentTo(["1", "2", "3", "4"]);
        (await Client.SDiffAsync("a", "b")).Should().BeEquivalentTo(["1", "2"]);
        (await Client.SMoveAsync("a", "b", "1")).Should().BeTrue();
        (await Client.SCardAsync("b")).Should().Be(3);
        (await Client.SPopAsync("a")).Should().BeOneOf("2", "3");
        (await Client.SRemAsync("b", "1", "3", "4")).Should().Be(3);
        (await Client.ExistsAsync("b")).Should().Be(0);
    }

    [Fact]
    public async Task SortedSets_WorkAsALeaderboard()
    {
        await Client.ZAddAsync("scores", [new("ada", 120), new("bob", 95), new("cy", 130)]);
        (await Client.ZIncrByAsync("scores", "bob", 40)).Should().Be(135);

        (await Client.ZRangeAsync("scores", 0, 1, descending: true)).Should().Equal(
            new RedisSortedSetEntry("bob", 135),
            new RedisSortedSetEntry("cy", 130));
        (await Client.ZRankAsync("scores", "ada")).Should().Be(0);
        (await Client.ZRankAsync("scores", "ada", descending: true)).Should().Be(2);
        (await Client.ZRankAsync("scores", "nobody")).Should().BeNull();
        (await Client.ZScoreAsync("scores", "cy")).Should().Be(130);
        (await Client.ZCountAsync("scores", 125, double.PositiveInfinity)).Should().Be(2);
        (await Client.ZRangeByScoreAsync("scores", 100, 132)).Select(e => e.Member).Should().Equal("ada", "cy");
        (await Client.ZRangeByScoreAsync("scores", descending: true, count: 1)).Single().Member.Should().Be("bob");
        (await Client.ZPopAsync("scores", 1)).Should().Equal(new RedisSortedSetEntry("ada", 120));
        (await Client.ZCardAsync("scores")).Should().Be(2);
    }

    [Fact]
    public async Task InfiniteScores_RoundTrip()
    {
        await Client.ZAddAsync("z", "top", double.PositiveInfinity);
        await Client.ZAddAsync("z", "bottom", double.NegativeInfinity);

        (await Client.ZRangeAsync("z")).Should().Equal(
            new RedisSortedSetEntry("bottom", double.NegativeInfinity),
            new RedisSortedSetEntry("top", double.PositiveInfinity));
    }




}
