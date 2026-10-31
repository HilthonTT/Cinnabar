using Cinnabar.Client;
using Cinnabar.Client.PubSub;

namespace Cinnabar.IntegrationTests;

/// <summary>PSUBSCRIBE through the client: pattern matches, pmessage delivery, mixed subscriptions.</summary>
public sealed class PatternPubSubTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task PatternSubscriber_ReceivesMatchingChannelsOnly()
    {
        await using RedisSubscriber subscriber = await Client.PSubscribeAsync("news.*");
        await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();

        (await Client.PublishAsync("weather", "ignored")).Should().Be(0);
        (await Client.PublishAsync("news.tech", "hello")).Should().Be(1);

        (await messages.NextAsync()).Should().Be(new RedisMessage("news.tech", "hello", "news.*"));
    }

    [Fact]
    public async Task ChannelAndMatchingPattern_DeliverTwice()
    {
        await using RedisSubscriber subscriber = await Client.SubscribeAsync("news.tech");
        await subscriber.PSubscribeAsync("news.*");
        await Wait.UntilAsync(
            async () => (await Client.ExecuteAsync("PUBSUB", "NUMPAT")).AsLong() == 1,
            "the pattern subscription is registered");
        await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();

        (await Client.PublishAsync("news.tech", "x")).Should().Be(2);

        var received = new[] { await messages.NextAsync(), await messages.NextAsync() };
        received.Should().BeEquivalentTo([new RedisMessage("news.tech", "x"), new RedisMessage("news.tech", "x", "news.*")]);
    }

    [Fact]
    public async Task PUnsubscribe_StopsDelivery()
    {
        await using RedisSubscriber subscriber = await Client.PSubscribeAsync("a*", "b*");

        await subscriber.PUnsubscribeAsync("a*");
        await Wait.UntilAsync(
            async () => (await Client.ExecuteAsync("PUBSUB", "NUMPAT")).AsLong() == 1,
            "one pattern remains");

        (await Client.PublishAsync("apple", "x")).Should().Be(0);
        (await Client.PublishAsync("banana", "y")).Should().Be(1);
    }

    [Fact]
    public async Task Disconnect_RemovesPatternSubscriptions()
    {
        RedisSubscriber subscriber = await Client.PSubscribeAsync("p*");
        await subscriber.DisposeAsync();

        await Wait.UntilAsync(
            async () => (await Client.ExecuteAsync("PUBSUB", "NUMPAT")).AsLong() == 0,
            "the server drops the closed connection's patterns");
    }
}
