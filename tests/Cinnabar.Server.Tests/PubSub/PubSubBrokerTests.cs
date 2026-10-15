using Cinnabar.Server.Networking;
using Cinnabar.Server.PubSub;

namespace Cinnabar.Server.Tests.PubSub;

public sealed class PubSubBrokerTests
{
    private readonly PubSubBroker _broker = new();

    private static ClientSession NewSession() => new(socket: null, requiresAuthentication: false);

    [Fact]
    public void Subscribe_returns_session_channel_count()
    {
        ClientSession session = NewSession();

        _broker.Subscribe(session, "a").Should().Be(1);
        _broker.Subscribe(session, "b").Should().Be(2);
        _broker.Subscribe(session, "a").Should().Be(2);
    }

    [Fact]
    public void Subscribe_records_channels_on_session()
    {
        ClientSession session = NewSession();

        _broker.Subscribe(session, "a");

        session.Channels.Should().BeEquivalentTo(["a"]);
        session.InSubscribedMode.Should().BeTrue();
    }

    [Fact]
    public void Channel_count_counts_distinct_channels()
    {
        _broker.Subscribe(NewSession(), "a");
        _broker.Subscribe(NewSession(), "a");
        _broker.Subscribe(NewSession(), "b");

        _broker.ChannelCount.Should().Be(2);
    }

    [Fact]
    public void Unsubscribe_returns_remaining_count()
    {
        ClientSession session = NewSession();
        _broker.Subscribe(session, "a");
        _broker.Subscribe(session, "b");

        _broker.Unsubscribe(session, "a").Should().Be(1);
        _broker.Unsubscribe(session, "b").Should().Be(0);
        session.InSubscribedMode.Should().BeFalse();
    }

    [Fact]
    public void Unsubscribe_from_unknown_channel_is_harmless()
    {
        ClientSession session = NewSession();
        _broker.Subscribe(session, "a");

        _broker.Unsubscribe(session, "zzz").Should().Be(1);
    }

    [Fact]
    public void Channel_is_removed_when_last_subscriber_leaves()
    {
        ClientSession first = NewSession();
        ClientSession second = NewSession();
        _broker.Subscribe(first, "a");
        _broker.Subscribe(second, "a");

        _broker.Unsubscribe(first, "a");
        _broker.ChannelCount.Should().Be(1);

        _broker.Unsubscribe(second, "a");
        _broker.ChannelCount.Should().Be(0);
    }

    [Fact]
    public void UnsubscribeAll_returns_channels_in_ordinal_order()
    {
        ClientSession session = NewSession();
        _broker.Subscribe(session, "b");
        _broker.Subscribe(session, "C");
        _broker.Subscribe(session, "a");

        _broker.UnsubscribeAll(session).Should().Equal("C", "a", "b");

        session.Channels.Should().BeEmpty();
        _broker.ChannelCount.Should().Be(0);
    }

    [Fact]
    public void UnsubscribeAll_keeps_other_sessions_subscribed()
    {
        ClientSession leaving = NewSession();
        ClientSession staying = NewSession();
        _broker.Subscribe(leaving, "a");
        _broker.Subscribe(staying, "a");

        _broker.UnsubscribeAll(leaving);

        _broker.ChannelCount.Should().Be(1);
        staying.Channels.Should().Contain("a");
    }

    [Fact]
    public void UnsubscribeAll_without_subscriptions_returns_empty()
    {
        _broker.UnsubscribeAll(NewSession()).Should().BeEmpty();
    }

    [Fact]
    public void Publish_to_channel_without_subscribers_returns_zero()
    {
        _broker.Publish("nobody", "hi").Should().Be(0);
    }

    [Fact]
    public void Publish_does_not_count_sessions_without_socket()
    {
        // TryPush returns false for a session without a socket, so it is not counted as a
        // receiver. Delivery to real sockets is covered by the integration tests.
        _broker.Subscribe(NewSession(), "a");

        _broker.Publish("a", "hi").Should().Be(0);
    }

    [Fact]
    public void Socketless_session_refuses_pushes()
    {
        NewSession().TryPush([1, 2, 3]).Should().BeFalse();
    }
}
