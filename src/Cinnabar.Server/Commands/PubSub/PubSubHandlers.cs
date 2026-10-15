using Cinnabar.Server.Networking;
using Cinnabar.Server.PubSub;

namespace Cinnabar.Server.Commands.PubSub;

/// <summary>
/// <c>SUBSCRIBE channel [channel ...]</c>: enters subscribed mode. Replies with one
/// <c>["subscribe", channel, count]</c> push per channel; messages then arrive as
/// <c>["message", channel, payload]</c>.
/// </summary>
[Command("SUBSCRIBE", MinArgs = 1, Flags = CommandFlags.PubSub, Group = "pubsub", Syntax = "channel [channel ...]")]
internal sealed class SubscribeHandler(PubSubBroker broker) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var replies = command.Arguments
            .Select(channel => PushReply("subscribe", channel, broker.Subscribe(session, channel)))
            .ToList();

        return ValueTask.FromResult(Reply.Sequence(replies));
    }

    /// <summary><c>[kind, channel, count]</c>, with the count as an integer, as Redis sends it.</summary>
    internal static Reply PushReply(string kind, string channel, int count) =>
        Reply.Array([Reply.BulkString(kind), Reply.BulkString(channel), Reply.Integer(count)]);
}

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

/// <summary><c>PUBLISH channel message</c>: returns the number of subscribers that received it.</summary>
[Command("PUBLISH", MinArgs = 2, MaxArgs = 2, Group = "pubsub", Syntax = "channel message")]
internal sealed class PublishHandler(PubSubBroker broker) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(broker.Publish(command[0], command[1])));
}
