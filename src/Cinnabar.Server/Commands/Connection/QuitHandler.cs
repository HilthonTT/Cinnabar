using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Connection;

/// <summary><c>QUIT</c>: replies OK and closes the connection.</summary>
[Command("QUIT", Flags = CommandFlags.NoAuth | CommandFlags.PubSub | CommandFlags.Transaction, Group = "connection")]
internal sealed class QuitHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Ok.ThenClose());
}
