using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>
/// <c>XADD key id field value [field value ...]</c>: appends an entry and returns its ID.
/// The ID may be <c>*</c> (auto), <c>ms-*</c> (auto sequence) or explicit <c>ms-seq</c>.
/// </summary>
/// <remarks>Replicated with the generated ID, so replicas store identical IDs.</remarks>
[Command("XADD", MinArgs = 4, Flags = CommandFlags.Write, Group = "stream", Syntax = "key id field value [field value ...]")]
internal sealed class XAddHandler(Keyspace keyspace, KeyNotifier notifier) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if ((command.Count - 2) % 2 != 0)
        {
            return ValueTask.FromResult(Reply.Error("ERR wrong number of arguments for 'xadd' command"));
        }

        string key = command[0];
        var fields = new List<KeyValuePair<string, string>>((command.Count - 2) / 2);
        for (int i = 2; i < command.Count; i += 2)
        {
            fields.Add(new KeyValuePair<string, string>(command[i], command[i + 1]));
        }

        StreamId id = keyspace.StreamAdd(key, command[1], fields);
        notifier.Notify(key);

        string idText = id.ToString();
        var propagated = new Command("XADD", [key, idText, .. command.Arguments.Skip(2)]);
        return ValueTask.FromResult(Reply.BulkString(idText).WithPropagation(propagated));
    }
}
