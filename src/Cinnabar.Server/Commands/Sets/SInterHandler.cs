using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SINTER key [key ...]</c>: members present in every set.</summary>
[Command("SINTER", MinArgs = 1, Group = "set", Syntax = "key [key ...]")]
internal sealed class SInterHandler(Keyspace keyspace) : SetCombineHandlerBase(keyspace, SetOperation.Intersect);
