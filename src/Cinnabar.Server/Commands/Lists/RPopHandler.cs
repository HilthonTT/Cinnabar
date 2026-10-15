using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>RPOP key [count]</c>: removes from the tail. Nil if the key does not exist.</summary>
[Command("RPOP", MinArgs = 1, MaxArgs = 2, Flags = CommandFlags.Write, Group = "list", Syntax = "key [count]")]
internal sealed class RPopHandler(Keyspace keyspace) : PopHandlerBase(keyspace, ListEnd.Right);
