using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>Shared logic for ZPOPMIN and ZPOPMAX.</summary>
internal abstract class PopHandlerBase(Keyspace keyspace, bool highest) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        int count = command.Count == 2 ? Arguments.ParseCount(command[1]) : 1;
        IReadOnlyList<ScoredMember> popped = keyspace.SortedSetPop(command[0], count, highest);

        Reply reply = SortedSetReplies.Members(popped, withScores: true);
        return ValueTask.FromResult(popped.Count > 0 ? reply : reply.WithoutPropagation());
    }
}
