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

        return ValueTask.FromResult(Reply.BulkString(ByteString.FromText(text.ToString())));
    }
}
