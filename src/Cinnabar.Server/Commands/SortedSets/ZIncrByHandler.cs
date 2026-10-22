using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary><c>ZINCRBY key increment member</c>: adds to a member's score (from 0 if new); returns the new score.</summary>
[Command("ZINCRBY", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "sorted-set", Syntax = "key increment member")]
internal sealed class ZIncrByHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        double increment = Arguments.ParseDouble(command[1]);
        SortedSetAddResult result = keyspace.SortedSetAdd(
            command[0],
            [new ScoredMember(command[2], increment)],
            new SortedSetAddOptions(Increment: true));

        return ValueTask.FromResult(SortedSetReplies.Score(result.Score));
    }
}
