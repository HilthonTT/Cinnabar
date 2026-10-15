using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>Shared logic for LPUSH and RPUSH.</summary>
internal abstract class PushHandlerBase(Keyspace keyspace, KeyNotifier notifier, ListEnd end) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];
        long length = keyspace.ListPush(key, command.Arguments.Skip(1), end);
        notifier.Notify(key);
        return ValueTask.FromResult(Reply.Integer(length));
    }
}
