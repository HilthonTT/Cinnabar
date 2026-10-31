using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>RPOPLPUSH source destination</c>: the same as <c>LMOVE source destination RIGHT LEFT</c>.</summary>
[Command("RPOPLPUSH", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "source destination")]
internal sealed class RPopLPushHandler(Keyspace keyspace, KeyNotifier notifier) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string? value = LMoveHandler.Move(keyspace, notifier, command[0], command[1], ListEnd.Right, ListEnd.Left);
        return ValueTask.FromResult(value is null
            ? Reply.NullBulkString.WithoutPropagation()
            : Reply.BulkString(value).WithPropagation(LMoveHandler.AsLMove(command[0], command[1], ListEnd.Right, ListEnd.Left)));
    }
}
