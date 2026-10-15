using Cinnabar.Server.PubSub;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

/// <remarks>
/// In Redis the third element of a (un)subscribe confirmation is an integer
/// (<c>:1</c>), not a bulk string; client libraries rely on that.
/// </remarks>
public sealed class PubSubCommandTests : DispatcherTestBase
{
    private static string Confirmation(string kind, string? channel, int count) =>
        $"*3\r\n{Bulk(kind)}{Bulk(channel)}:{count}\r\n";

    [Fact]
    public async Task Subscribe_replies_once_per_channel_with_running_count()
    {
        (await Run("SUBSCRIBE", "a", "b")).Should().Be(
            Confirmation("subscribe", "a", 1) + Confirmation("subscribe", "b", 2));
    }

    [Fact]
    public async Task Subscribe_twice_to_same_channel_keeps_count()
    {
        await Run("SUBSCRIBE", "a");

        (await Run("SUBSCRIBE", "a")).Should().Be(Confirmation("subscribe", "a", 1));
    }

    [Fact]
    public async Task Subscribe_enters_subscribed_mode()
    {
        await Run("SUBSCRIBE", "a");

        H.Session.InSubscribedMode.Should().BeTrue();
        H.Session.Channels.Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task Unsubscribe_named_channels_reports_remaining_count()
    {
        await Run("SUBSCRIBE", "a", "b");

        (await Run("UNSUBSCRIBE", "a")).Should().Be(Confirmation("unsubscribe", "a", 1));
        (await Run("UNSUBSCRIBE", "b")).Should().Be(Confirmation("unsubscribe", "b", 0));
        H.Session.InSubscribedMode.Should().BeFalse();
    }

    [Fact]
    public async Task Unsubscribe_without_arguments_leaves_all_channels_in_name_order()
    {
        await Run("SUBSCRIBE", "zeta", "alpha");

        (await Run("UNSUBSCRIBE")).Should().Be(
            Confirmation("unsubscribe", "alpha", 1) + Confirmation("unsubscribe", "zeta", 0));
        H.Get<PubSubBroker>().ChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task Unsubscribe_without_any_subscription_replies_with_nil_channel()
    {
        (await Run("UNSUBSCRIBE")).Should().Be("*3\r\n$11\r\nunsubscribe\r\n$-1\r\n:0\r\n");
    }

    [Fact]
    public async Task Publish_without_subscribers_returns_zero()
    {
        (await Run("PUBLISH", "news", "hello")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Publish_to_socketless_subscriber_counts_zero()
    {
        // A session without a socket cannot receive pushes (TryPush returns false), so it
        // does not count as a receiver. Delivery to real sockets is covered by integration tests.
        await H.Dispatch(H.NewSession(), "SUBSCRIBE", "news");

        (await Run("PUBLISH", "news", "hello")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Ping_in_subscribed_mode_replies_with_pong_array()
    {
        await Run("SUBSCRIBE", "a");

        (await Run("PING")).Should().Be(BulkArray("pong", ""));
        (await Run("PING", "hi")).Should().Be(BulkArray("pong", "hi"));
    }

    [Fact]
    public async Task Publish_is_rejected_in_subscribed_mode()
    {
        await Run("SUBSCRIBE", "a");

        (await Run("PUBLISH", "a", "x")).Should().StartWith("-ERR Can't execute 'publish'");
    }
}
