using Cinnabar.Server.Networking;
using Cinnabar.Server.PubSub;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.PubSub;

/// <summary>
/// <c>PUBSUB CHANNELS [pattern]</c>, <c>PUBSUB NUMSUB [channel ...]</c>, <c>PUBSUB NUMPAT</c>:
/// introspects the pub/sub state.
/// </summary>
[Command("PUBSUB", MinArgs = 1, Group = "pubsub", Syntax = "CHANNELS [pattern] | NUMSUB [channel ...] | NUMPAT")]
internal sealed class PubSubHandler(PubSubBroker broker) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string subcommand = command[0];

        if (Arguments.Is(subcommand, "CHANNELS") && command.Count <= 2)
        {
            string? pattern = command.Count == 2 ? command[1] : null;
            return ValueTask.FromResult(Reply.BulkStringArray(broker.ActiveChannels(pattern)));
        }

        if (Arguments.Is(subcommand, "NUMSUB"))
        {
            var replies = new List<Reply>();
            foreach (string channel in command.Arguments.Skip(1))
            {
                replies.Add(Reply.BulkString(channel));
                replies.Add(Reply.Integer(broker.SubscriberCount(channel)));
            }

            return ValueTask.FromResult(Reply.Array(replies));
        }

        if (Arguments.Is(subcommand, "NUMPAT") && command.Count == 1)
        {
            return ValueTask.FromResult(Reply.Integer(broker.PatternCount));
        }

        throw new StorageException(
            $"ERR unknown subcommand or wrong number of arguments for '{ByteString.ToText(subcommand)}'. Try PUBSUB HELP.");
    }
}
