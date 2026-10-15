using Cinnabar.Client;
using Cinnabar.Client.PubSub;
using Cinnabar.Protocol;

namespace Cinnabar.IntegrationTests;

public sealed class PubSubTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Subscriber_ReceivesMessagePublishedAfterSubscribe()
    {
        await using RedisSubscriber subscriber = await Client.SubscribeAsync("news");
        await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();

        (await Client.PublishAsync("news", "hello")).Should().Be(1);

        (await messages.NextAsync()).Should().Be(new RedisMessage("news", "hello"));
    }

    [Fact]
    public async Task Subscriber_ReceivesMessagesInOrder()
    {
        await using RedisSubscriber subscriber = await Client.SubscribeAsync("seq");
        await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();

        for (int i = 0; i < 50; i++)
        {
            await Client.PublishAsync("seq", i.ToString());
        }

        for (int i = 0; i < 50; i++)
        {
            (await messages.NextAsync()).Message.Should().Be(i.ToString());
        }
    }

    [Fact]
    public async Task SeveralChannels_MessagesCarryTheirChannel()
    {
        await using RedisSubscriber subscriber = await Client.SubscribeAsync("a", "b");
        await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();

        await Client.PublishAsync("b", "to-b");
        await Client.PublishAsync("a", "to-a");

        (await messages.NextAsync()).Should().Be(new RedisMessage("b", "to-b"));
        (await messages.NextAsync()).Should().Be(new RedisMessage("a", "to-a"));
    }

    [Fact]
    public async Task Publish_FansOutToEverySubscriber()
    {
        await using RedisSubscriber first = await Client.SubscribeAsync("room");
        await using RedisSubscriber second = await Client.SubscribeAsync("room");
        await using RedisSubscriber third = await Client.SubscribeAsync("room");

        (await Client.PublishAsync("room", "hi all")).Should().Be(3);

        foreach (RedisSubscriber subscriber in new[] { first, second, third })
        {
            await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();
            (await messages.NextAsync()).Should().Be(new RedisMessage("room", "hi all"));
        }
    }

    [Fact]
    public async Task Publish_WithoutSubscribers_ReturnsZero()
    {
        (await Client.PublishAsync("nobody", "x")).Should().Be(0);
    }

    [Fact]
    public async Task Unsubscribe_StopsDelivery()
    {
        await using RedisSubscriber subscriber = await Client.SubscribeAsync("muted", "marker");
        await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();

        await subscriber.UnsubscribeAsync("muted");
        await Wait.UntilAsync(async () => await Client.PublishAsync("muted", "probe") == 0, "the unsubscribe takes effect");

        (await Client.PublishAsync("muted", "after")).Should().Be(0);
        (await Client.PublishAsync("marker", "end")).Should().Be(1);

        // Probes published before the unsubscribe landed may arrive; "after" must not.
        var received = new List<RedisMessage>();
        RedisMessage message;
        do
        {
            message = await messages.NextAsync();
            received.Add(message);
        }
        while (message.Channel != "marker");

        received.Should().NotContain(new RedisMessage("muted", "after"));
        received[^1].Should().Be(new RedisMessage("marker", "end"));
    }

    [Fact]
    public async Task DisposingSubscriber_RemovesSubscription()
    {
        RedisSubscriber subscriber = await Client.SubscribeAsync("gone");
        (await Client.PublishAsync("gone", "1")).Should().Be(1);

        await subscriber.DisposeAsync();

        await Wait.UntilAsync(async () => await Client.PublishAsync("gone", "2") == 0, "the server notices the closed subscriber");
    }

    [Fact]
    public async Task DisposingSubscriber_Repeatedly_NeverThrows()
    {
        // Disposal races the background read loop; repeat to expose it.
        for (int i = 0; i < 200; i++)
        {
            RedisSubscriber subscriber = await Client.SubscribeAsync("churn");
            Func<Task> dispose = async () => await subscriber.DisposeAsync();
            await dispose.Should().NotThrowAsync($"iteration {i}");
        }
    }

    [Fact]
    public async Task DisposingSubscriber_CompletesMessageStream()
    {
        RedisSubscriber subscriber = await Client.SubscribeAsync("c");
        await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();

        await subscriber.DisposeAsync();

        (await messages.MoveNextAsync().AsTask().WithTimeout()).Should().BeFalse();
    }

    [Theory]
    [InlineData("with \"quotes\" and 'single'")]
    [InlineData("multi\r\nline\nmessage")]
    [InlineData("unicode ✓ 😀 日本語 ü")]
    [InlineData("")]
    [InlineData("$5\r\nfake\r\n*1\r\n")]
    public async Task Message_ArrivesIntact(string payload)
    {
        await using RedisSubscriber subscriber = await Client.SubscribeAsync("raw");
        await using IAsyncEnumerator<RedisMessage> messages = subscriber.Messages().GetAsyncEnumerator();

        await Client.PublishAsync("raw", payload);

        (await messages.NextAsync()).Message.Should().Be(payload);
    }

    [Fact]
    public async Task SubscribedRawConnection_RejectsGet_AndAnswersPingWithArray()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);

        RespValue confirmation = await connection.CommandAsync("SUBSCRIBE", "ch");
        confirmation.AsStringList().Should().Equal("subscribe", "ch", "1");

        RespValue error = await connection.CommandAsync("GET", "x");
        error.IsError.Should().BeTrue();
        error.Text.Should().Contain("only (P|S)SUBSCRIBE");

        RespValue pong = await connection.CommandAsync("PING");
        pong.Type.Should().Be(RespType.Array);
        pong.AsStringList().Should().Equal("pong", "");

        // Still subscribed after the rejected command.
        (await _server.Client.PublishAsync("ch", "m")).Should().Be(1);
        (await connection.ReadAsync()).AsStringList().Should().Equal("message", "ch", "m");
    }

    [Fact]
    public async Task RawConnection_UnsubscribeAll_LeavesSubscribedMode()
    {
        await using RawConnection connection = await RawConnection.ConnectAsync(_server.Port);
        await connection.SendAsync("SUBSCRIBE", "a", "b");
        (await connection.ReadAsync()).AsStringList().Should().Equal("subscribe", "a", "1");
        (await connection.ReadAsync()).AsStringList().Should().Equal("subscribe", "b", "2");

        await connection.SendAsync("UNSUBSCRIBE");
        (await connection.ReadAsync()).AsStringList().Should().Equal("unsubscribe", "a", "1");
        (await connection.ReadAsync()).AsStringList().Should().Equal("unsubscribe", "b", "0");

        (await connection.CommandAsync("PING")).AsString().Should().Be("PONG");
        (await connection.CommandAsync("GET", "x")).IsNull.Should().BeTrue();
    }
}
