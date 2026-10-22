using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>
/// <c>SRANDMEMBER key [count]</c>: random members, without removing them. A positive count
/// returns distinct members; a negative count returns <c>|count|</c> members that may repeat.
/// </summary>
[Command("SRANDMEMBER", MinArgs = 1, MaxArgs = 2, Group = "set", Syntax = "key [count]")]
internal sealed class SRandMemberHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (command.Count == 1)
        {
            IReadOnlyList<string> one = keyspace.SetRandomMembers(command[0], 1);
            return ValueTask.FromResult(Reply.BulkString(one.Count > 0 ? one[0] : null));
        }

        long count = Arguments.ParseLong(command[1]);
        if (count < -int.MaxValue)
        {
            throw new StorageException("ERR value is out of range");
        }

        return ValueTask.FromResult(Reply.BulkStringArray(keyspace.SetRandomMembers(command[0], count)));
    }
}
