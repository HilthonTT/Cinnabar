using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary>
/// <c>HMSET key field value [field value ...]</c>: the deprecated form of HSET; replies OK.
/// </summary>
[Command("HMSET", MinArgs = 3, Flags = CommandFlags.Write, Group = "hash", Syntax = "key field value [field value ...]")]
internal sealed class HMSetHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (command.Count % 2 != 1)
        {
            throw Arguments.WrongArity(command);
        }

        keyspace.HashSetFields(command[0], Arguments.Pairs(command, 1));
        return ValueTask.FromResult(Reply.Ok);
    }
}
