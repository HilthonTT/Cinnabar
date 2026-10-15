using System.Globalization;
using System.Text.Json;

namespace Cinnabar.Server.Configuration;

/// <summary>
/// Builds <see cref="ServerSettings"/> from command-line arguments, optionally layered on
/// top of a JSON file passed with <c>--config</c>. Flags always win over the file.
/// </summary>
public static class CommandLine
{
    /// <summary>Text printed by <c>--help</c>.</summary>
    public const string Usage =
        """
        Usage: Cinnabar.Server [options]   (or: dotnet run --project src/Cinnabar.Server -- [options])

        Options:
          --config <file>          Load settings from a JSON file (flags override it)
          --bind <address>         IP address to listen on            (default 0.0.0.0)
          --port <port>            TCP port                           (default 6379)
          --requirepass <pass>     Require AUTH with this password
          --dir <path>             Snapshot directory                 (default ./data)
          --dbfilename <name>      Snapshot file name                 (default dump.rdb)
          --save-interval <sec>    Save a snapshot every N seconds    (default 0 = off)
          --replicaof <host> <port>
                                   Run as a replica of the given master
                                   ("<host> <port>" and "<host>:<port>" also work)
          --masterauth <pass>      Password for the master
          --metrics-port <port>    Prometheus endpoint port, 0 = off  (default 9090)
          --loglevel <level>       Trace|Debug|Information|Warning|Error (default Information)
          --help                   Show this help
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses <paramref name="args"/>.</summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="showHelp">Set when <c>--help</c> was passed.</param>
    /// <exception cref="ArgumentException">An argument is unknown or has an invalid value.</exception>
    public static ServerSettings Parse(IReadOnlyList<string> args, out bool showHelp)
    {
        showHelp = false;
        ServerSettings settings = LoadConfigFile(args) ?? new ServerSettings();

        for (int i = 0; i < args.Count; i++)
        {
            string flag = args[i];
            switch (flag.ToLowerInvariant())
            {
                case "--help" or "-h" or "-?":
                    showHelp = true;
                    break;
                case "--config":
                    Next(args, ref i, flag); // already applied by LoadConfigFile
                    break;
                case "--bind":
                    settings.Network.BindAddress = Next(args, ref i, flag);
                    break;
                case "--port":
                    settings.Network.Port = ParsePort(Next(args, ref i, flag), flag, allowZero: true);
                    break;
                case "--requirepass":
                    settings.Security.RequirePass = Next(args, ref i, flag);
                    break;
                case "--dir":
                    settings.Persistence.Directory = Next(args, ref i, flag);
                    break;
                case "--dbfilename":
                    settings.Persistence.DbFileName = Next(args, ref i, flag);
                    break;
                case "--save-interval":
                    settings.Persistence.SaveIntervalSeconds = ParseInt(Next(args, ref i, flag), flag, min: 0);
                    break;
                case "--replicaof":
                    ApplyReplicaOf(args, ref i, settings.Replication);
                    break;
                case "--masterauth":
                    settings.Replication.MasterAuth = Next(args, ref i, flag);
                    break;
                case "--metrics-port":
                    settings.Diagnostics.MetricsPort = ParsePort(Next(args, ref i, flag), flag, allowZero: true);
                    break;
                case "--loglevel":
                    settings.Diagnostics.LogLevel = Next(args, ref i, flag);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{flag}'. Run with --help to see the options.");
            }
        }

        return settings;
    }

    private static ServerSettings? LoadConfigFile(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (!args[i].Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string path = args[i + 1];
            if (!File.Exists(path))
            {
                throw new ArgumentException($"Config file '{path}' does not exist.");
            }

            try
            {
                return JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(path), JsonOptions)
                       ?? new ServerSettings();
            }
            catch (JsonException e)
            {
                throw new ArgumentException($"Config file '{path}' is not valid JSON: {e.Message}");
            }
        }

        return null;
    }

    /// <summary>Accepts <c>host port</c> (two tokens), <c>"host port"</c> or <c>host:port</c>.</summary>
    private static void ApplyReplicaOf(IReadOnlyList<string> args, ref int i, ReplicationSettings replication)
    {
        string first = Next(args, ref i, "--replicaof");
        string host;
        string port;

        if (first.Contains(' '))
        {
            string[] parts = first.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            (host, port) = parts.Length == 2 ? (parts[0], parts[1]) : (first, "");
        }
        else if (first.LastIndexOf(':') is var colon and > 0)
        {
            (host, port) = (first[..colon], first[(colon + 1)..]);
        }
        else
        {
            host = first;
            port = Next(args, ref i, "--replicaof");
        }

        if (host.Equals("no", StringComparison.OrdinalIgnoreCase) && port.Equals("one", StringComparison.OrdinalIgnoreCase))
        {
            replication.MasterHost = null;
            return;
        }

        replication.MasterHost = host;
        replication.MasterPort = ParsePort(port, "--replicaof", allowZero: false);
    }

    private static string Next(IReadOnlyList<string> args, ref int i, string flag)
    {
        if (i + 1 >= args.Count)
        {
            throw new ArgumentException($"Option '{flag}' needs a value.");
        }

        return args[++i];
    }

    private static int ParsePort(string text, string flag, bool allowZero)
    {
        int port = ParseInt(text, flag, min: allowZero ? 0 : 1);
        if (port > 65535)
        {
            throw new ArgumentException($"Invalid value '{text}' for {flag}: a port must be between {(allowZero ? 0 : 1)} and 65535.");
        }

        return port;
    }

    private static int ParseInt(string text, string flag, int min)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min)
        {
            throw new ArgumentException($"Invalid value '{text}' for {flag}: expected an integer >= {min}.");
        }

        return value;
    }
}
