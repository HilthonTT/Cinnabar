using Cinnabar.Server.Configuration;
using Cinnabar.Server.Diagnostics;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Persistence;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace Cinnabar.Server.Hosting;

/// <summary>
/// A complete server instance: storage, networking, persistence, replication and metrics.
/// Several instances can run in one process (the integration tests start a master and a
/// replica side by side).
/// </summary>
/// <example>
/// <code>
/// await using var server = CinnabarServer.Create(new ServerSettings());
/// await server.StartAsync();
/// Console.WriteLine($"Listening on {server.Port}");
/// </code>
/// </example>
public sealed class CinnabarServer : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly ILogger<CinnabarServer> _logger;
    private MetricServer? _metricServer;
    private bool _started;
    private bool _loaded;
    private bool _stopped;

    private CinnabarServer(ServiceProvider services)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<CinnabarServer>>();
    }

    /// <summary>The settings this server was created with.</summary>
    public ServerSettings Settings => _services.GetRequiredService<ServerSettings>();

    /// <summary>The TCP port actually bound; valid after <see cref="StartAsync"/>.</summary>
    public int Port => _services.GetRequiredService<ServerState>().Port;

    /// <summary>The service container, for tests and diagnostics.</summary>
    internal IServiceProvider Services => _services;

    /// <summary>The keyspace, for tests and embedding.</summary>
    public Keyspace Keyspace => _services.GetRequiredService<Keyspace>();

    /// <summary>Builds a server. Nothing is bound or loaded until <see cref="StartAsync"/>.</summary>
    /// <param name="settings">Server configuration.</param>
    /// <param name="configureLogging">Optional logging setup; defaults to a console logger at the configured level.</param>
    public static CinnabarServer Create(ServerSettings settings, Action<ILoggingBuilder>? configureLogging = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (configureLogging is not null)
            {
                configureLogging(logging);
                return;
            }

            logging.SetMinimumLevel(ParseLogLevel(settings.Diagnostics.LogLevel));
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
        });
        services.AddCinnabarServer(settings);

        return new CinnabarServer(services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));
    }

    /// <summary>
    /// Loads the snapshot, binds the port, and starts replication, periodic saves and metrics.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException("The server has already been started.");
        }

        _started = true;
        ServerSettings settings = Settings;

        await _services.GetRequiredService<SnapshotService>().LoadAsync(cancellationToken);

        // Only now may shutdown save: if loading failed, saving the empty keyspace would
        // overwrite the snapshot we just refused to load.
        _loaded = true;

        TcpServer tcp = _services.GetRequiredService<TcpServer>();
        tcp.Start();
        ServerState state = _services.GetRequiredService<ServerState>();
        state.Port = tcp.LocalEndPoint.Port;

        if (settings.Replication.IsReplica)
        {
            _services.GetRequiredService<ReplicationClient>().Start(state.Port);
        }

        _services.GetRequiredService<SnapshotService>().StartPeriodicSaves();
        StartMetrics(settings);

        string role = settings.Replication.IsReplica
            ? $"replica of {settings.Replication.MasterHost}:{settings.Replication.MasterPort}"
            : "master";
        _logger.LogInformation("Cinnabar {Version} ready on port {Port} as {Role}.", state.Version, state.Port, role);
    }

    /// <summary>Starts, then runs until <paramref name="cancellationToken"/> is cancelled, then stops.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Shutting down...");
        }

        await StopAsync();
    }

    /// <summary>
    /// Stops accepting connections, closes clients, stops replication, and writes a final
    /// snapshot if <c>SaveOnShutdown</c> is enabled.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_started || _stopped)
        {
            return;
        }

        _stopped = true;

        await _services.GetRequiredService<TcpServer>().DisposeAsync();
        await _services.GetRequiredService<ReplicationClient>().DisposeAsync();
        await _services.GetRequiredService<SnapshotService>().DisposeAsync();

        if (_metricServer is not null)
        {
            await _metricServer.StopAsync();
        }

        if (Settings.Persistence.SaveOnShutdown && _loaded)
        {
            try
            {
                await _services.GetRequiredService<SnapshotService>().SaveAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Final save failed.");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _services.GetRequiredService<Keyspace>().Dispose();
        await _services.DisposeAsync();
    }

    private void StartMetrics(ServerSettings settings)
    {
        int port = settings.Diagnostics.MetricsPort;
        if (port <= 0)
        {
            return;
        }

        Keyspace keyspace = Keyspace;
        ServerMetrics metrics = _services.GetRequiredService<ServerMetrics>();
        Metrics.DefaultRegistry.AddBeforeCollectCallback(() => metrics.Keys.Set(keyspace.Count));

        try
        {
            _metricServer = new MetricServer(port: port);
            _metricServer.Start();
            _logger.LogInformation("Prometheus metrics on http://localhost:{Port}/metrics.", port);
        }
        catch (Exception e)
        {
            // Metrics are optional; don't refuse to serve data because the port is taken.
            _metricServer = null;
            _logger.LogWarning("Metrics endpoint disabled: could not listen on port {Port}: {Reason}", port, e.Message);
        }
    }

    private static LogLevel ParseLogLevel(string level) =>
        Enum.TryParse(level, ignoreCase: true, out LogLevel parsed) ? parsed : LogLevel.Information;
}
