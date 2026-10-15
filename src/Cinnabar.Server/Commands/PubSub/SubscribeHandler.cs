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
