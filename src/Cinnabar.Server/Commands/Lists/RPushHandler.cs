using Cinnabar.Server.Blocking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>RPUSH key element [element ...]</c>: pushes to the tail; returns the new length.</summary>
[Command("RPUSH", MinArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key element [element ...]")]
internal sealed class RPushHandler(Keyspace keyspace, KeyNotifier notifier) : PushHandlerBase(keyspace, notifier, ListEnd.Right);
