using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SDIFF key [key ...]</c>: members of the first set that are in none of the others.</summary>
[Command("SDIFF", MinArgs = 1, Group = "set", Syntax = "key [key ...]")]
internal sealed class SDiffHandler(Keyspace keyspace) : SetCombineHandlerBase(keyspace, SetOperation.Difference);
