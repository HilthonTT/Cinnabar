using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary>
/// <c>HSET key field value [field value ...]</c>: sets fields, creating the hash if needed.
/// Returns the number of fields that were added.
/// </summary>
[Command("HSET", MinArgs = 3, Flags = CommandFlags.Write, Group = "hash", Syntax = "key field value [field value ...]")]
internal sealed class HSetHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (command.Count % 2 != 1)
        {
            throw Arguments.WrongArity(command);
        }

        return ValueTask.FromResult(Reply.Integer(keyspace.HashSetFields(command[0], Arguments.Pairs(command, 1))));
    }
}
