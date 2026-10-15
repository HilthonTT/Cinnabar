using Cinnabar.Server.Networking;
using Cinnabar.Server.PubSub;

namespace Cinnabar.Server.Commands.PubSub;

/// <summary><c>PUBLISH channel message</c>: returns the number of subscribers that received it.</summary>
[Command("PUBLISH", MinArgs = 2, MaxArgs = 2, Group = "pubsub", Syntax = "channel message")]
internal sealed class PublishHandler(PubSubBroker broker) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(broker.Publish(command[0], command[1])));
}
