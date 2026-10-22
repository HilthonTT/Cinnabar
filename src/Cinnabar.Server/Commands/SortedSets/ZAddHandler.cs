using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>
/// <c>ZADD key [NX | XX] [GT | LT] [CH] [INCR] score member [score member ...]</c>: adds
/// members or updates their scores. Returns the number of new members (or, with CH, of new
/// or changed ones). With INCR, behaves like ZINCRBY and returns the new score, or nil if a
/// flag prevented the update.
/// </summary>
[Command("ZADD", MinArgs = 3, Flags = CommandFlags.Write, Group = "sorted-set",
    Syntax = "key [NX | XX] [GT | LT] [CH] [INCR] score member [score member ...]")]
internal sealed class ZAddHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        bool nx = false, xx = false, gt = false, lt = false, ch = false, incr = false;
        int index = 1;
        for (; index < command.Count; index++)
        {
            switch (command[index].ToUpperInvariant())
            {
                case "NX": nx = true; continue;
                case "XX": xx = true; continue;
                case "GT": gt = true; continue;
                case "LT": lt = true; continue;
                case "CH": ch = true; continue;
                case "INCR": incr = true; continue;
            }

            break;
        }

        int remaining = command.Count - index;
        if (remaining == 0 || remaining % 2 != 0)
        {
            return ValueTask.FromResult(Reply.Error(Arguments.SyntaxError));
        }

        if (nx && xx)
        {
            return ValueTask.FromResult(Reply.Error("ERR XX and NX options at the same time are not compatible"));
        }

        if ((gt && lt) || (nx && (gt || lt)))
        {
            return ValueTask.FromResult(Reply.Error("ERR GT, LT, and/or NX options at the same time are not compatible"));
        }

        if (incr && remaining > 2)
        {
            return ValueTask.FromResult(Reply.Error("ERR INCR option supports a single increment-element pair"));
        }

        var members = new List<ScoredMember>(remaining / 2);
        for (int i = index; i < command.Count; i += 2)
        {
            members.Add(new ScoredMember(command[i + 1], Arguments.ParseDouble(command[i])));
        }

        var options = new SortedSetAddOptions(
            nx ? SetCondition.IfNotExists : xx ? SetCondition.IfExists : SetCondition.Always,
            gt ? ScoreComparison.GreaterThan : lt ? ScoreComparison.LessThan : ScoreComparison.Any,
            incr);

        SortedSetAddResult result = keyspace.SortedSetAdd(command[0], members, options);
        Reply reply = incr ? SortedSetReplies.Score(result.Score) : Reply.Integer(ch ? result.Changed : result.Added);
        return ValueTask.FromResult(result.Changed > 0 ? reply : reply.WithoutPropagation());
    }
}
