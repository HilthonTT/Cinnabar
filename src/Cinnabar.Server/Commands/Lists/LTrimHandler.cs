using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>LTRIM key start stop</c>: keeps only the inclusive range; negative indexes count from the end.</summary>
[Command("LTRIM", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write | CommandFlags.FreesMemory, Group = "list", Syntax = "key start stop")]
internal sealed class LTrimHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        long start = Arguments.ParseLong(command[1]);
        long stop = Arguments.ParseLong(command[2]);
        return ValueTask.FromResult(keyspace.ListTrim(command[0], start, stop) ? Reply.Ok : Reply.Ok.WithoutPropagation());
    }
}
