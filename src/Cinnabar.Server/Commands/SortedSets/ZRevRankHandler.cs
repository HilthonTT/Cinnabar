using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZREVRANK key member [WITHSCORE]</c>: the member's 0-based rank by descending score, or nil.</summary>
[Command("ZREVRANK", MinArgs = 2, MaxArgs = 3, Group = "sorted-set", Syntax = "key member [WITHSCORE]")]
internal sealed class ZRevRankHandler(Keyspace keyspace) : RankHandlerBase(keyspace, reverse: true);
