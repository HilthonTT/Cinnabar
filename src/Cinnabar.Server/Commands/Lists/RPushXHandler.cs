using Cinnabar.Server.Blocking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>RPUSHX key element [element ...]</c>: RPUSH only if the list exists; returns the new length, or 0.</summary>
[Command("RPUSHX", MinArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key element [element ...]")]
internal sealed class RPushXHandler(Keyspace keyspace, KeyNotifier notifier) : PushIfExistsHandlerBase(keyspace, notifier, ListEnd.Right);
