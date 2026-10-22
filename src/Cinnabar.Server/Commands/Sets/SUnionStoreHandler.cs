using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>
/// <c>SUNIONSTORE destination key [key ...]</c>: stores the union in <c>destination</c>
/// (replacing it) and returns its size.
/// </summary>
[Command("SUNIONSTORE", MinArgs = 2, Flags = CommandFlags.Write, Group = "set", Syntax = "destination key [key ...]")]
internal sealed class SUnionStoreHandler(Keyspace keyspace) : SetCombineStoreHandlerBase(keyspace, SetOperation.Union);
