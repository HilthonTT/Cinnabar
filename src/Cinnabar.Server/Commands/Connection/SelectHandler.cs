using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Connection;

/// <summary><c>SELECT index</c>: only database 0 exists.</summary>
[Command("SELECT", MinArgs = 1, MaxArgs = 1, Group = "connection", Syntax = "index")]
internal sealed class SelectHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Arguments.ParseLong(command[0]) == 0
            ? Reply.Ok
            : Reply.Error("ERR DB index is out of range"));
}
