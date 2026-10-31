using Cinnabar.Server.Blocking;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>BRPOPLPUSH source destination timeout</c>: blocking RPOPLPUSH.</summary>
[Command("BRPOPLPUSH", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write | CommandFlags.Blocking, Group = "list", Syntax = "source destination timeout")]
internal sealed class BRPopLPushHandler(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    TransactionGate gate,
    ServerSettings settings)
    : BlockingMoveHandlerBase(keyspace, notifier, master, gate, settings)
{
    protected override (string, string, ListEnd, ListEnd, TimeSpan?) Parse(Command command) =>
        (command[0], command[1], ListEnd.Right, ListEnd.Left, Arguments.ParseTimeoutSeconds(command[2]));
}
