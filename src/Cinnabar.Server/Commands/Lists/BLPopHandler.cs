using Cinnabar.Server.Blocking;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>BLPOP key [key ...] timeout</c>: blocking LPOP. Timeout in seconds; 0 waits forever.</summary>
[Command("BLPOP", MinArgs = 2, Flags = CommandFlags.Write | CommandFlags.Blocking, Group = "list", Syntax = "key [key ...] timeout")]
internal sealed class BLPopHandler(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    TransactionGate gate,
    ServerSettings settings)
    : BlockingPopHandlerBase(keyspace, notifier, master, gate, settings, ListEnd.Left);
