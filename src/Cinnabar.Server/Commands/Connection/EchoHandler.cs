using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Connection;

/// <summary><c>ECHO message</c>: returns the message as a bulk string.</summary>
[Command("ECHO", MinArgs = 1, MaxArgs = 1, Group = "connection", Syntax = "message")]
internal sealed class EchoHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkString(command[0]));
}
