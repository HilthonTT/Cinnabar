using System.Security.Cryptography;
using System.Text;
using Cinnabar.Server.Configuration;
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

/// <summary><c>ECHO message</c>: returns the message as a bulk string.</summary>
[Command("ECHO", MinArgs = 1, MaxArgs = 1, Group = "connection", Syntax = "message")]
internal sealed class EchoHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkString(command[0]));
}

/// <summary>
/// <c>AUTH [username] password</c>: authenticates the connection against <c>requirepass</c>.
/// The username, when given, must match the configured one (default <c>default</c>).
/// </summary>
[Command("AUTH", MinArgs = 1, MaxArgs = 2, Flags = CommandFlags.NoAuth, Group = "connection", Syntax = "[username] password")]
internal sealed class AuthHandler(ServerSettings settings) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        SecuritySettings security = settings.Security;
        if (!security.RequiresAuthentication)
        {
            return ValueTask.FromResult(Reply.Error(
                "ERR AUTH <password> called without any password configured for the default user. Are you sure your configuration is correct?"));
        }

        string username = command.Count == 2 ? command[0] : security.Username;
        string password = command[^1];

        // Compare both parts in constant time, and always compare both.
        bool userMatches = FixedTimeEquals(username, security.Username);
        bool passwordMatches = FixedTimeEquals(password, security.RequirePass!);
        if (!(userMatches & passwordMatches))
        {
            return ValueTask.FromResult(Reply.Error("WRONGPASS invalid username-password pair or user is disabled."));
        }

        session.IsAuthenticated = true;
        return ValueTask.FromResult(Reply.Ok);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

/// <summary><c>QUIT</c>: replies OK and closes the connection.</summary>
[Command("QUIT", Flags = CommandFlags.NoAuth | CommandFlags.PubSub | CommandFlags.Transaction, Group = "connection")]
internal sealed class QuitHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Ok.ThenClose());
}

/// <summary><c>SELECT index</c>: only database 0 exists.</summary>
[Command("SELECT", MinArgs = 1, MaxArgs = 1, Group = "connection", Syntax = "index")]
internal sealed class SelectHandler : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Arguments.ParseLong(command[0]) == 0
            ? Reply.Ok
            : Reply.Error("ERR DB index is out of range"));
}

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
