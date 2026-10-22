using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZSCORE key member</c>: the member's score, or nil.</summary>
[Command("ZSCORE", MinArgs = 2, MaxArgs = 2, Group = "sorted-set", Syntax = "key member")]
internal sealed class ZScoreHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(SortedSetReplies.Score(keyspace.SortedSetScores(command[0], [command[1]])[0]));
}
