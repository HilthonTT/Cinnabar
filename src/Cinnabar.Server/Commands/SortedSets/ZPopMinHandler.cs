using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZPOPMIN key [count]</c>: removes and returns the lowest-scored members, as <c>[member, score, ...]</c>.</summary>
[Command("ZPOPMIN", MinArgs = 1, MaxArgs = 2, Flags = CommandFlags.Write, Group = "sorted-set", Syntax = "key [count]")]
internal sealed class ZPopMinHandler(Keyspace keyspace) : PopHandlerBase(keyspace, highest: false);
