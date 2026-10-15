using System.Globalization;
using System.Text;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Hosting;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Persistence;
using Cinnabar.Server.PubSub;
using Cinnabar.Server.Replication;
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
                result.Add(value);
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

/// <summary><c>INFO [section]</c>: server, clients, replication, persistence and keyspace facts.</summary>
[Command("INFO", MaxArgs = 1, Group = "server", Syntax = "[section]")]
internal sealed class InfoHandler(
    ServerSettings settings,
    ServerState state,
    Keyspace keyspace,
    ReplicationMaster master,
    ReplicationClient replicationClient,
    SnapshotService snapshots,
    PubSubBroker broker,
    Security.IpGuard ipGuard) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string section = command.Count == 1 ? command[0].ToLowerInvariant() : "all";
        var text = new StringBuilder();

        void Section(string name, params (string Key, object Value)[] fields)
        {
            if (section is not ("all" or "default" or "everything") && section != name.ToLowerInvariant())
            {
                return;
            }

            text.Append("# ").Append(name).Append("\r\n");
            foreach ((string key, object value) in fields)
            {
                text.Append(key).Append(':').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append("\r\n");
            }

            text.Append("\r\n");
        }

        Section("Server",
            ("cinnabar_version", state.Version),
            ("redis_version", "7.2.0"),
            ("os", Environment.OSVersion.ToString()),
            ("process_id", Environment.ProcessId),
            ("tcp_port", state.Port),
            ("uptime_in_seconds", (long)state.Uptime.TotalSeconds));

        Section("Clients",
            ("connected_clients", ipGuard.TotalConnections),
            ("pubsub_channels", broker.ChannelCount));

        Section("Persistence",
            ("rdb_last_save_time", snapshots.LastSave?.ToUnixTimeSeconds() ?? 0),
            ("rdb_bgsave_in_progress", snapshots.IsBackgroundSaveRunning ? 1 : 0));

        if (settings.Replication.IsReplica)
        {
            Section("Replication",
                ("role", "slave"),
                ("master_host", settings.Replication.MasterHost!),
                ("master_port", settings.Replication.MasterPort),
                ("master_link_status", replicationClient.IsLinkUp ? "up" : "down"),
                ("master_replid", replicationClient.MasterReplicationId ?? ""),
                ("slave_repl_offset", replicationClient.ProcessedOffset));
        }
        else
        {
            IReadOnlyCollection<ConnectedReplica> replicas = master.Replicas;
            var fields = new List<(string, object)>
            {
                ("role", "master"),
                ("connected_slaves", replicas.Count),
                ("master_replid", master.ReplicationId),
                ("master_repl_offset", master.Offset),
            };

            int index = 0;
            foreach (ConnectedReplica replica in replicas)
            {
                fields.Add(($"slave{index++}", $"ip={replica.Session.RemoteAddress},state=online,offset={replica.AckOffset}"));
            }

            Section("Replication", [.. fields]);
        }

        Section("Keyspace", ("db0", $"keys={keyspace.Keys().Count},expires=0"));

        return ValueTask.FromResult(Reply.BulkString(text.ToString()));
    }
}

/// <summary>
/// <c>COMMAND [COUNT | DOCS | INFO ...]</c>: COUNT returns the number of commands; other
/// forms return the command names. Enough for redis-cli and client libraries that probe it.
/// </summary>
[Command("COMMAND", Group = "server", Syntax = "[COUNT | DOCS | INFO [command ...]]")]
internal sealed class CommandHandler(Lazy<CommandDispatcher> dispatcher) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        List<CommandAttribute> commands = [.. dispatcher.Value.Commands];

        if (command.Count > 0 && Arguments.Is(command[0], "COUNT"))
        {
            return ValueTask.FromResult(Reply.Integer(commands.Count));
        }

        if (command.Count > 0 && Arguments.Is(command[0], "DOCS"))
        {
            // Name → empty doc map; redis-cli only needs the names for hints.
            return ValueTask.FromResult(Reply.Array(commands.SelectMany(c => new[]
            {
                Reply.BulkString(c.Name.ToLowerInvariant()),
                Reply.EmptyArray,
            })));
        }

        return ValueTask.FromResult(Reply.BulkStringArray(commands.Select(c => c.Name.ToLowerInvariant())));
    }
}

/// <summary><c>SAVE</c>: writes the snapshot synchronously.</summary>
[Command("SAVE", MaxArgs = 0, Group = "server")]
internal sealed class SaveHandler(SnapshotService snapshots) : ICommandHandler
{
    public async ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        await snapshots.SaveAsync(cancellationToken);
        return Reply.Ok;
    }
}

/// <summary><c>BGSAVE</c>: writes the snapshot in the background.</summary>
[Command("BGSAVE", MaxArgs = 1, Group = "server", Syntax = "[SCHEDULE]")]
internal sealed class BgSaveHandler(SnapshotService snapshots) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(snapshots.TryStartBackgroundSave()
            ? Reply.SimpleString("Background saving started")
            : Reply.Error("ERR Background save already in progress"));
}

/// <summary><c>LASTSAVE</c>: Unix time of the last successful save, 0 if none.</summary>
[Command("LASTSAVE", MaxArgs = 0, Group = "server")]
internal sealed class LastSaveHandler(SnapshotService snapshots) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(snapshots.LastSave?.ToUnixTimeSeconds() ?? 0));
}
