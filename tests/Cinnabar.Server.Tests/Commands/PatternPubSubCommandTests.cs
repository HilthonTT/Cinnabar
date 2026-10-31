using Cinnabar.Server.PubSub;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class PatternPubSubCommandTests : DispatcherTestBase
{
    private static string Confirmation(string kind, string? name, int count) =>
        $"*3\r\n{Bulk(kind)}{Bulk(name)}:{count}\r\n";

    [Fact]
    public async Task Psubscribe_counts_channels_and_patterns_together()
    {
        await Run("SUBSCRIBE", "a");

        (await Run("PSUBSCRIBE", "news.*", "user:?")).Should().Be(
            Confirmation("psubscribe", "news.*", 2) + Confirmation("psubscribe", "user:?", 3));
        H.Session.Patterns.Should().BeEquivalentTo(["news.*", "user:?"]);
    }

    [Fact]
    public async Task Patterns_keep_the_session_in_subscribed_mode()
    {
        await Run("PSUBSCRIBE", "p*");

        H.Session.InSubscribedMode.Should().BeTrue();
        (await Run("GET", "k")).Should().StartWith("-ERR Can't execute 'get'");
        (await Run("UNSUBSCRIBE")).Should().Be(Confirmation("unsubscribe", null, 1));
        H.Session.InSubscribedMode.Should().BeTrue();
    }

    [Fact]
    public async Task Punsubscribe_named_patterns_reports_remaining_count()
    {
        await Run("PSUBSCRIBE", "a*", "b*");

        (await Run("PUNSUBSCRIBE", "a*")).Should().Be(Confirmation("punsubscribe", "a*", 1));
        (await Run("PUNSUBSCRIBE", "b*")).Should().Be(Confirmation("punsubscribe", "b*", 0));
        H.Session.InSubscribedMode.Should().BeFalse();
    }

    [Fact]
    public async Task Punsubscribe_without_arguments_leaves_every_pattern_in_order()
    {
        await Run("SUBSCRIBE", "chan");
        await Run("PSUBSCRIBE", "b*", "a*");

        (await Run("PUNSUBSCRIBE")).Should().Be(
            Confirmation("punsubscribe", "a*", 2) + Confirmation("punsubscribe", "b*", 1));
        H.Get<PubSubBroker>().PatternCount.Should().Be(0);
    }

    [Fact]
    public async Task Punsubscribe_without_patterns_replies_with_null()
    {
        (await Run("PUNSUBSCRIBE")).Should().Be(Confirmation("punsubscribe", null, 0));
    }

    [Fact]
    public async Task Pubsub_introspection()
    {
        var first = H.NewSession();
        var second = H.NewSession();
        await H.Dispatch(first, "SUBSCRIBE", "news", "sport");
        await H.Dispatch(second, "SUBSCRIBE", "news");
        await H.Dispatch(second, "PSUBSCRIBE", "n*");

        (await Run("PUBSUB", "CHANNELS")).Should().Be(BulkArray("news", "sport"));
        (await Run("PUBSUB", "channels", "s*")).Should().Be(BulkArray("sport"));
        (await Run("PUBSUB", "NUMSUB", "news", "none")).Should().Be(
            "*4\r\n" + Bulk("news") + ":2\r\n" + Bulk("none") + ":0\r\n");
        (await Run("PUBSUB", "NUMPAT")).Should().Be(":1\r\n");
    }

    [Fact]
    public async Task Pubsub_unknown_subcommand_is_an_error()
    {
        (await Run("PUBSUB", "NOPE")).Should().StartWith("-ERR unknown subcommand or wrong number of arguments for 'NOPE'");
    }
}
