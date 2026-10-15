namespace Cinnabar.Server.Configuration;

/// <summary>
/// Everything the server can be configured with. Every property has a default, so a JSON
/// settings file only needs to contain what it changes. See <c>docs/configuration.md</c>.
/// </summary>
public sealed class ServerSettings
{
    /// <summary>Listening address and port.</summary>
    public NetworkSettings Network { get; set; } = new();

    /// <summary>RDB snapshot location and save policy.</summary>
    public PersistenceSettings Persistence { get; set; } = new();

    /// <summary>Master/replica configuration.</summary>
    public ReplicationSettings Replication { get; set; } = new();

    /// <summary>Authentication, connection limits and rate limiting.</summary>
    public SecuritySettings Security { get; set; } = new();

    /// <summary>Logging and metrics.</summary>
    public DiagnosticsSettings Diagnostics { get; set; } = new();
}

/// <summary>Where the server listens.</summary>
public sealed class NetworkSettings
{
    /// <summary>IP address to bind. Defaults to all IPv4 interfaces.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>TCP port. 0 picks a free port (useful in tests).</summary>
    public int Port { get; set; } = 6379;
}

/// <summary>RDB snapshot settings.</summary>
public sealed class PersistenceSettings
{
    /// <summary>Directory holding the snapshot file. Defaults to <c>./data</c>.</summary>
    public string Directory { get; set; } = "data";

    /// <summary>Snapshot file name.</summary>
    public string DbFileName { get; set; } = "dump.rdb";

    /// <summary>Load the snapshot when the server starts.</summary>
    public bool LoadOnStartup { get; set; } = true;

    /// <summary>Write a snapshot during a graceful shutdown (Ctrl+C / SIGTERM).</summary>
    public bool SaveOnShutdown { get; set; } = true;

    /// <summary>Write a snapshot every N seconds. 0 disables periodic saves.</summary>
    public int SaveIntervalSeconds { get; set; }

    /// <summary>Full path of the snapshot file.</summary>
    public string FilePath => Path.GetFullPath(Path.Combine(Directory, DbFileName));
}

/// <summary>Master/replica configuration.</summary>
public sealed class ReplicationSettings
{
    /// <summary>Host of the master to replicate from. <see langword="null"/> means this server is a master.</summary>
    public string? MasterHost { get; set; }

    /// <summary>Port of the master to replicate from.</summary>
    public int MasterPort { get; set; } = 6379;

    /// <summary>Password sent to the master when it requires authentication.</summary>
    public string? MasterAuth { get; set; }

    /// <summary><see langword="true"/> when <see cref="MasterHost"/> is set.</summary>
    public bool IsReplica => !string.IsNullOrWhiteSpace(MasterHost);
}

/// <summary>Authentication and abuse protection.</summary>
public sealed class SecuritySettings
{
    /// <summary>Password clients must send with AUTH. Empty or <see langword="null"/> disables authentication.</summary>
    public string? RequirePass { get; set; }

    /// <summary>The only username AUTH accepts (Redis 6 ACL syntax <c>AUTH user pass</c>).</summary>
    public string Username { get; set; } = "default";

    /// <summary>Maximum simultaneous connections in total.</summary>
    public int MaxConnections { get; set; } = 10_000;

    /// <summary>Maximum simultaneous connections from one IP address.</summary>
    public int MaxConnectionsPerIp { get; set; } = 1_000;

    /// <summary>Sustained commands per second allowed per IP address. 0 disables rate limiting.</summary>
    public double RateLimitPerSecond { get; set; } = 100_000;

    /// <summary>Burst size (token bucket capacity) per IP address.</summary>
    public int RateLimitBurst { get; set; } = 10_000;

    /// <summary><see langword="true"/> when <see cref="RequirePass"/> is set.</summary>
    public bool RequiresAuthentication => !string.IsNullOrEmpty(RequirePass);
}

/// <summary>Logging and metrics.</summary>
public sealed class DiagnosticsSettings
{
    /// <summary>Minimum log level: Trace, Debug, Information, Warning, Error, Critical or None.</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>Port for the Prometheus <c>/metrics</c> endpoint. 0 disables it.</summary>
    public int MetricsPort { get; set; } = 9090;
}
