using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZMSCORE key member [member ...]</c>: each member's score, or nil.</summary>
[Command("ZMSCORE", MinArgs = 2, Group = "sorted-set", Syntax = "key member [member ...]")]
internal sealed class ZMScoreHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(
            keyspace.SortedSetScores(command[0], [.. command.Arguments.Skip(1)])
                .Select(score => score is { } value ? Arguments.Format(value) : null)));
}
