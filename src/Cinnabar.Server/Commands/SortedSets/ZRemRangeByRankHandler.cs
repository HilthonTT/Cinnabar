using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZREMRANGEBYRANK key start stop</c>: removes members between two ranks; returns how many.</summary>
[Command("ZREMRANGEBYRANK", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "sorted-set", Syntax = "key start stop")]
internal sealed class ZRemRangeByRankHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Removed(keyspace.SortedSetRemoveRangeByRank(
            command[0], Arguments.ParseLong(command[1]), Arguments.ParseLong(command[2]))));

    internal static Reply Removed(long count) => count > 0 ? Reply.Integer(count) : Reply.Zero.WithoutPropagation();
}
