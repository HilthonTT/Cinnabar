using Cinnabar.Server.Networking;
using Cinnabar.Server.PubSub;

namespace Cinnabar.Server.Commands.PubSub;

/// <summary>
/// <c>PUNSUBSCRIBE [pattern ...]</c>: leaves the given pattern subscriptions (matched
/// literally), or all of them. Leaving the last channel and pattern ends subscribed mode.
/// </summary>
[Command("PUNSUBSCRIBE", Flags = CommandFlags.PubSub, Group = "pubsub", Syntax = "[pattern [pattern ...]]")]
internal sealed class PUnsubscribeHandler(PubSubBroker broker) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var replies = new List<Reply>();

        if (command.Count == 0)
        {
            IReadOnlyList<string> patterns = broker.PatternUnsubscribeAll(session);
            if (patterns.Count == 0)
            {
                replies.Add(Reply.Array([Reply.BulkString("punsubscribe"), Reply.NullBulkString, Reply.Integer(session.SubscriptionCount)]));
            }

            for (int i = 0; i < patterns.Count; i++)
            {
                replies.Add(SubscribeHandler.PushReply("punsubscribe", patterns[i], patterns.Count - i - 1 + session.Channels.Count));
            }
        }
        else
        {
            foreach (string pattern in command.Arguments)
            {
                int remaining = broker.PatternUnsubscribe(session, pattern);
                replies.Add(SubscribeHandler.PushReply("punsubscribe", pattern, remaining));
            }
        }

        return ValueTask.FromResult(Reply.Sequence(replies));
    }
}
