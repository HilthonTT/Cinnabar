using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>FLUSHDB [ASYNC | SYNC]</c>: removes every key.</summary>
[Command("FLUSHDB", MaxArgs = 1, Flags = CommandFlags.Write, Group = "server", Syntax = "[ASYNC | SYNC]")]
internal sealed class FlushDbHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (command.Count == 1 && !Arguments.Is(command[0], "ASYNC") && !Arguments.Is(command[0], "SYNC"))
        {
            return ValueTask.FromResult(Reply.Error(Arguments.SyntaxError));
        }

        keyspace.Clear();
        return ValueTask.FromResult(Reply.Ok);
    }
}
