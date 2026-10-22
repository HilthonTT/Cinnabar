using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZREMRANGEBYLEX key min max</c>: removes members between two lexicographic bounds; returns how many.</summary>
[Command("ZREMRANGEBYLEX", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "sorted-set", Syntax = "key min max")]
internal sealed class ZRemRangeByLexHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ZRemRangeByRankHandler.Removed(keyspace.SortedSetRemoveRangeByLex(
            command[0], Arguments.ParseLexBound(command[1]), Arguments.ParseLexBound(command[2]))));
}
