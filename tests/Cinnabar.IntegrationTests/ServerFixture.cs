using Cinnabar.Client;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Hosting;
using Microsoft.Extensions.Logging;

namespace Cinnabar.IntegrationTests;

/// <summary>
/// Starts a real server on a free port with persistence and metrics disabled, plus a client
/// connected to it. Each test class gets its own instance.
/// </summary>
public sealed class TestServer : IAsyncDisposable
{
    private TestServer(CinnabarServer server, RedisClient client)
    {
        Server = server;
        Client = client;
    }

    public CinnabarServer Server { get; }

    public RedisClient Client { get; }

    public int Port => Server.Port;

    private readonly List<RedisClient> _extraClients = [];

    /// <summary>Creates another client for this server; it is disposed with the server.</summary>
    public RedisClient CreateClient(Action<RedisClientOptions>? configure = null)
    {
        var options = new RedisClientOptions { Host = "127.0.0.1", Port = Port };
        configure?.Invoke(options);
        var client = new RedisClient(options);
        lock (_extraClients)
        {
            _extraClients.Add(client);
        }

        return client;
    }

    public static async Task<TestServer> StartAsync(Action<ServerSettings>? configure = null, string? password = null)
    {
        ServerSettings settings = CreateSettings();
        configure?.Invoke(settings);

        CinnabarServer server = CinnabarServer.Create(settings, logging => logging.SetMinimumLevel(LogLevel.Warning));
        await server.StartAsync();

        var client = new RedisClient(new RedisClientOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            Password = password,
        });

        return new TestServer(server, client);
    }

    /// <summary>Settings for an isolated in-process server.</summary>
    public static ServerSettings CreateSettings() => new()
    {
        Network = { BindAddress = "127.0.0.1", Port = 0 },
        Persistence =
        {
            Directory = Path.Combine(Path.GetTempPath(), "cinnabar-tests", Guid.NewGuid().ToString("N")),
            LoadOnStartup = false,
            SaveOnShutdown = false,
        },
        Diagnostics = { MetricsPort = 0 },
    };

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        RedisClient[] extra;
        lock (_extraClients)
        {
            extra = [.. _extraClients];
        }

        foreach (RedisClient client in extra)
        {
            await client.DisposeAsync();
        }

        await Server.DisposeAsync();
    }
}
