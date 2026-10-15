using Cinnabar.Server.Networking;
using Cinnabar.Server.PubSub;

namespace Cinnabar.Server.Commands.PubSub;

/// <summary>
/// <c>UNSUBSCRIBE [channel ...]</c>: leaves the given channels, or all of them. Leaving the
/// last channel ends subscribed mode.
/// </summary>
[Command("UNSUBSCRIBE", Flags = CommandFlags.PubSub, Group = "pubsub", Syntax = "[channel [channel ...]]")]
internal sealed class UnsubscribeHandler(PubSubBroker broker) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var replies = new List<Reply>();

        if (command.Count == 0)
        {
            IReadOnlyList<string> channels = broker.UnsubscribeAll(session);
            if (channels.Count == 0)
            {
                replies.Add(Reply.Array([Reply.BulkString("unsubscribe"), Reply.NullBulkString, Reply.Zero]));
            }

            for (int i = 0; i < channels.Count; i++)
            {
                replies.Add(SubscribeHandler.PushReply("unsubscribe", channels[i], channels.Count - i - 1));
            }
        }
        else
        {
            foreach (string channel in command.Arguments)
            {
                int remaining = broker.Unsubscribe(session, channel);
                replies.Add(SubscribeHandler.PushReply("unsubscribe", channel, remaining));
            }
        }

        return ValueTask.FromResult(Reply.Sequence(replies));
    }
}
