using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Connection;

/// <summary>
/// <c>CLIENT ID | GETNAME | SETNAME name | SETINFO attr value</c>: the subset that client
/// libraries send while connecting.
/// </summary>
[Command("CLIENT", MinArgs = 1, Group = "connection", Syntax = "ID | GETNAME | SETNAME name | SETINFO attribute value")]
internal sealed class ClientHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string subcommand = command[0].ToUpperInvariant();
        Reply reply = (subcommand, command.Count) switch
        {
            ("ID", 1) => Reply.Integer(session.Id),
            ("GETNAME", 1) => Reply.BulkString(session.Name),
            ("SETNAME", 2) when !command[1].Any(char.IsWhiteSpace) => SetName(session, command[1]),
            ("SETNAME", 2) => Reply.Error("ERR Client names cannot contain spaces, newlines or special characters."),
            ("SETINFO", 3) => Reply.Ok,
            _ => Reply.Error($"ERR unknown subcommand or wrong number of arguments for '{command[0]}'. Try CLIENT HELP."),
        };

        return ValueTask.FromResult(reply);
    }

    private static Reply SetName(ClientSession session, string name)
    {
        session.Name = name.Length == 0 ? null : name;
        return Reply.Ok;
    }
}
