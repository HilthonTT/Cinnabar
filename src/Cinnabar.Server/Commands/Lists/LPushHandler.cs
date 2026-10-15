using Cinnabar.Server.Blocking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>LPUSH key element [element ...]</c>: pushes to the head; returns the new length.</summary>
[Command("LPUSH", MinArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key element [element ...]")]
internal sealed class LPushHandler(Keyspace keyspace, KeyNotifier notifier) : PushHandlerBase(keyspace, notifier, ListEnd.Left);
