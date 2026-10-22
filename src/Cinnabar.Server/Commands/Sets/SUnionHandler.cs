using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SUNION key [key ...]</c>: members present in any set.</summary>
[Command("SUNION", MinArgs = 1, Group = "set", Syntax = "key [key ...]")]
internal sealed class SUnionHandler(Keyspace keyspace) : SetCombineHandlerBase(keyspace, SetOperation.Union);
