using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary>
/// <c>MSET key value [key value ...]</c>: sets several strings atomically, replacing values of
/// any type and clearing TTLs.
/// </summary>
[Command("MSET", MinArgs = 2, Flags = CommandFlags.Write, Group = "string", Syntax = "key value [key value ...]")]
internal sealed class MSetHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (command.Count % 2 != 0)
        {
            throw Arguments.WrongArity(command);
        }

        keyspace.SetStrings(Arguments.Pairs(command, 0));
        return ValueTask.FromResult(Reply.Ok);
    }
}
