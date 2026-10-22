using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>
/// <c>SDIFFSTORE destination key [key ...]</c>: stores the difference in <c>destination</c>
/// (replacing it) and returns its size.
/// </summary>
[Command("SDIFFSTORE", MinArgs = 2, Flags = CommandFlags.Write, Group = "set", Syntax = "destination key [key ...]")]
internal sealed class SDiffStoreHandler(Keyspace keyspace) : SetCombineStoreHandlerBase(keyspace, SetOperation.Difference);
