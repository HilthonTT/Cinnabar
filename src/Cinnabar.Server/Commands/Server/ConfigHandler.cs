using Cinnabar.Server.Configuration;
using Cinnabar.Server.Hosting;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Server;

/// <summary>
/// <c>CONFIG GET pattern [pattern ...]</c>: name/value pairs for matching parameters.
/// CONFIG SET is not supported; configure the server with flags or a JSON file.
/// </summary>
[Command("CONFIG", MinArgs = 1, Group = "server", Syntax = "GET parameter [parameter ...]")]
internal sealed class ConfigHandler(ServerSettings settings, ServerState state) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (!Arguments.Is(command[0], "GET"))
        {
            return ValueTask.FromResult(Reply.Error(
                $"ERR CONFIG {command[0].ToUpperInvariant()} is not supported; only CONFIG GET is."));
        }

        if (command.Count < 2)
        {
            return ValueTask.FromResult(Reply.Error("ERR wrong number of arguments for 'config|get' command"));
        }

        Dictionary<string, string> parameters = Parameters();
        var result = new List<string?>();
        foreach ((string name, string value) in parameters)
        {
            if (command.Arguments.Skip(1).Any(pattern => KeyPattern.IsMatch(pattern.ToLowerInvariant(), name)))
            {
                result.Add(name);
                result.Add(ByteString.FromText(value));
            }
        }

        return ValueTask.FromResult(Reply.BulkStringArray(result));
    }

    private Dictionary<string, string> Parameters() => new(StringComparer.Ordinal)
    {
        ["bind"] = settings.Network.BindAddress,
        ["port"] = Arguments.Format(state.Port),
        ["dir"] = Path.GetFullPath(settings.Persistence.Directory),
        ["dbfilename"] = settings.Persistence.DbFileName,
        ["requirepass"] = settings.Security.RequirePass ?? "",
        ["masterauth"] = settings.Replication.MasterAuth ?? "",
        ["replicaof"] = settings.Replication.IsReplica
            ? $"{settings.Replication.MasterHost} {settings.Replication.MasterPort}"
            : "",
        ["maxclients"] = Arguments.Format(settings.Security.MaxConnections),
        ["save"] = settings.Persistence.SaveIntervalSeconds > 0
            ? $"{settings.Persistence.SaveIntervalSeconds} 0"
            : "",
        ["appendonly"] = "no",
        ["databases"] = "1",
    };
}
