using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>LPOP key [count]</c>: removes from the head. Nil if the key does not exist.</summary>
[Command("LPOP", MinArgs = 1, MaxArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key [count]")]
internal sealed class LPopHandler(Keyspace keyspace) : PopHandlerBase(keyspace, ListEnd.Left);
