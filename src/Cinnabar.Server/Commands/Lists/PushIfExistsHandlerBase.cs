using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>Shared logic for LPUSHX and RPUSHX: push only onto a list that already exists.</summary>
internal abstract class PushIfExistsHandlerBase(Keyspace keyspace, KeyNotifier notifier, ListEnd end) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];
        long length = keyspace.ListPushIfExists(key, command.Arguments.Skip(1), end);
        if (length == 0)
        {
            return ValueTask.FromResult(Reply.Zero.WithoutPropagation());
        }

        notifier.Notify(key);
        return ValueTask.FromResult(Reply.Integer(length));
    }
}
