using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZPOPMAX key [count]</c>: removes and returns the highest-scored members, as <c>[member, score, ...]</c>.</summary>
[Command("ZPOPMAX", MinArgs = 1, MaxArgs = 2, Flags = CommandFlags.Write, Group = "sorted-set", Syntax = "key [count]")]
internal sealed class ZPopMaxHandler(Keyspace keyspace) : PopHandlerBase(keyspace, highest: true);
