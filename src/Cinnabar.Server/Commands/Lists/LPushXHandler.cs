using Cinnabar.Server.Blocking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>LPUSHX key element [element ...]</c>: LPUSH only if the list exists; returns the new length, or 0.</summary>
[Command("LPUSHX", MinArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key element [element ...]")]
internal sealed class LPushXHandler(Keyspace keyspace, KeyNotifier notifier) : PushIfExistsHandlerBase(keyspace, notifier, ListEnd.Left);
