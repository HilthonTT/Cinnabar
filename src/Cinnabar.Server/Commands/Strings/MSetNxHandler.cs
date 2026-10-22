using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary>
/// <c>MSETNX key value [key value ...]</c>: like MSET, but writes nothing if any key exists.
/// Returns 1 if the keys were set, 0 otherwise.
/// </summary>
[Command("MSETNX", MinArgs = 2, Flags = CommandFlags.Write, Group = "string", Syntax = "key value [key value ...]")]
internal sealed class MSetNxHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (command.Count % 2 != 0)
        {
            throw Arguments.WrongArity(command);
        }

        return ValueTask.FromResult(keyspace.SetStrings(Arguments.Pairs(command, 0), onlyIfNoneExist: true)
            ? Reply.One
            : Reply.Zero.WithoutPropagation());
    }
}
