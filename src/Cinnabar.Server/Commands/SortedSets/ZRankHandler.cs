using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZRANK key member [WITHSCORE]</c>: the member's 0-based rank by ascending score, or nil.</summary>
[Command("ZRANK", MinArgs = 2, MaxArgs = 3, Group = "sorted-set", Syntax = "key member [WITHSCORE]")]
internal sealed class ZRankHandler(Keyspace keyspace) : RankHandlerBase(keyspace, reverse: false);
