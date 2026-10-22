using System.Security.Cryptography;
using System.Text;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Connection;

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

        // Arguments are byte strings; the configured credentials are text.
        byte[] username = command.Count == 2
            ? ByteString.GetBytes(command[0])
            : Encoding.UTF8.GetBytes(security.Username);
        byte[] password = ByteString.GetBytes(command[^1]);

        // Compare both parts in constant time, and always compare both.
        bool userMatches = FixedTimeEquals(username, Encoding.UTF8.GetBytes(security.Username));
        bool passwordMatches = FixedTimeEquals(password, Encoding.UTF8.GetBytes(security.RequirePass!));
        if (!(userMatches & passwordMatches))
        {
            return ValueTask.FromResult(Reply.Error("WRONGPASS invalid username-password pair or user is disabled."));
        }

        session.IsAuthenticated = true;
        return ValueTask.FromResult(Reply.Ok);
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(a), SHA256.HashData(b));
}
