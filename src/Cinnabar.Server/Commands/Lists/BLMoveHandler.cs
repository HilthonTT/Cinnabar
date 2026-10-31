using Cinnabar.Server.Blocking;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>
/// <c>BLMOVE source destination LEFT|RIGHT LEFT|RIGHT timeout</c>: blocking LMOVE. Timeout in
/// seconds; 0 waits forever.
/// </summary>
[Command("BLMOVE", MinArgs = 5, MaxArgs = 5, Flags = CommandFlags.Write | CommandFlags.Blocking, Group = "list", Syntax = "source destination LEFT|RIGHT LEFT|RIGHT timeout")]
internal sealed class BLMoveHandler(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    TransactionGate gate,
    ServerSettings settings)
    : BlockingMoveHandlerBase(keyspace, notifier, master, gate, settings)
{
    protected override (string, string, ListEnd, ListEnd, TimeSpan?) Parse(Command command) =>
        (command[0], command[1], LMoveHandler.ParseEnd(command[2]), LMoveHandler.ParseEnd(command[3]),
            Arguments.ParseTimeoutSeconds(command[4]));
}
