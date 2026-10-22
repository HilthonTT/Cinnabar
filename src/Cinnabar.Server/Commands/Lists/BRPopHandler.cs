using Cinnabar.Server.Blocking;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>BRPOP key [key ...] timeout</c>: blocking RPOP.</summary>
[Command("BRPOP", MinArgs = 2, Flags = CommandFlags.Write | CommandFlags.Blocking, Group = "list", Syntax = "key [key ...] timeout")]
internal sealed class BRPopHandler(
    Keyspace keyspace,
    KeyNotifier notifier,
    ReplicationMaster master,
    TransactionGate gate,
    ServerSettings settings)
    : BlockingPopHandlerBase(keyspace, notifier, master, gate, settings, ListEnd.Right);
