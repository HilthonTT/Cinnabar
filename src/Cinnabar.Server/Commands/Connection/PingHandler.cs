using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Connection;

/// <summary>
/// <c>PING [message]</c>: PONG, or the message. In subscribed mode replies
/// <c>["pong", message]</c>, as Redis does.
/// </summary>
[Command("PING", MaxArgs = 1, Flags = CommandFlags.PubSub, Group = "connection", Syntax = "[message]")]
internal sealed class PingHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string? message = command.Count == 1 ? command[0] : null;

        Reply reply = session.InSubscribedMode
            ? Reply.BulkStringArray(["pong", message ?? ""])
            : message is null ? Reply.SimpleString("PONG") : Reply.BulkString(message);

        return ValueTask.FromResult(reply);
    }
}
