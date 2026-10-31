using Cinnabar.Server.Networking;
using Cinnabar.Server.PubSub;

namespace Cinnabar.Server.Commands.PubSub;

/// <summary>
/// <c>PSUBSCRIBE pattern [pattern ...]</c>: subscribes to every channel matching a glob
/// pattern and enters subscribed mode. Replies with one <c>["psubscribe", pattern, count]</c>
/// push per pattern; messages then arrive as <c>["pmessage", pattern, channel, payload]</c>.
/// </summary>
[Command("PSUBSCRIBE", MinArgs = 1, Flags = CommandFlags.PubSub, Group = "pubsub", Syntax = "pattern [pattern ...]")]
internal sealed class PSubscribeHandler(PubSubBroker broker) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        var replies = command.Arguments
            .Select(pattern => SubscribeHandler.PushReply("psubscribe", pattern, broker.PatternSubscribe(session, pattern)))
            .ToList();

        return ValueTask.FromResult(Reply.Sequence(replies));
    }
}
