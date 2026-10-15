using Cinnabar.Client;

namespace Cinnabar.IntegrationTests;

public sealed class SmokeTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Ping_ReturnsPong()
    {
        (await Client.PingAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task SetThenGet_RoundTripsUnicode()
    {
        await Client.SetAsync("greeting", "héllo wörld ✓");
        (await Client.GetAsync("greeting")).Should().Be("héllo wörld ✓");
    }

    [Fact]
    public async Task ListAndStream_Work()
    {
        (await Client.RPushAsync("tasks", "a", "b", "c")).Should().Be(3);
        (await Client.LRangeAsync("tasks")).Should().Equal("a", "b", "c");

        string id = await Client.XAddAsync("events", [new("action", "click")]);
        id.Should().MatchRegex(@"^\d+-\d+$");
        (await Client.XRangeAsync("events")).Should().ContainSingle().Which.Id.Should().Be(id);
    }
}
