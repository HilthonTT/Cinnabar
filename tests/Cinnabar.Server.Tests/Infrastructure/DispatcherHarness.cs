using Cinnabar.Server.Commands;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Hosting;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Cinnabar.Server.Tests.Infrastructure;

/// <summary>
/// A full server container without sockets: dispatch commands straight into
/// <see cref="CommandDispatcher"/> and look at the encoded replies.
/// </summary>
internal sealed class DispatcherHarness : IAsyncDisposable
{
    /// <summary>The fake clock's starting point: 2025-01-01T00:00:00Z.</summary>
    public static readonly DateTimeOffset Start = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;

    public DispatcherHarness(Action<ServerSettings>? configure = null)
    {
        Settings = new ServerSettings();
        Settings.Diagnostics.MetricsPort = 0;
        Settings.Persistence.Directory = Path.Combine(Path.GetTempPath(), "cinnabar-server-tests", Guid.NewGuid().ToString("N"));
        configure?.Invoke(Settings);

        Time = new FakeTimeProvider(Start);

        IServiceCollection services = new ServiceCollection().AddLogging().AddCinnabarServer(Settings);
        services.AddSingleton<TimeProvider>(Time);
        _services = services.BuildServiceProvider();

        Dispatcher = _services.GetRequiredService<CommandDispatcher>();
        Session = NewSession();
    }

    public ServerSettings Settings { get; }

    public FakeTimeProvider Time { get; }

    public CommandDispatcher Dispatcher { get; }

    /// <summary>The default client session (authenticated unless requirepass is set).</summary>
    public ClientSession Session { get; }

    public Keyspace Keyspace => Get<Keyspace>();

    public ReplicationMaster Master => Get<ReplicationMaster>();

    public long NowMs => Time.GetUtcNow().ToUnixTimeMilliseconds();

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    public ClientSession NewSession() =>
        new(socket: null, requiresAuthentication: Settings.Security.RequiresAuthentication);

    /// <summary>Dispatches on the default session and returns the raw RESP text.</summary>
    public async Task<string> Run(params string[] parts) => (await Dispatch(Session, parts)).ToString();

    /// <summary>Dispatches on the default session.</summary>
    public Task<Reply> Dispatch(params string[] parts) => Dispatch(Session, parts);

    /// <summary>Dispatches on <paramref name="session"/>.</summary>
    public async Task<Reply> Dispatch(ClientSession session, params string[] parts)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await Dispatcher.DispatchAsync(new Command(parts[0], parts[1..]), session, timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        try
        {
            if (Directory.Exists(Settings.Persistence.Directory))
            {
                Directory.Delete(Settings.Persistence.Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}

/// <summary>Base class giving each test a fresh <see cref="DispatcherHarness"/>.</summary>
public abstract class DispatcherTestBase : IAsyncLifetime
{
    private readonly Lazy<DispatcherHarness> _harness;

    protected DispatcherTestBase(Action<ServerSettings>? configure = null)
    {
        _harness = new Lazy<DispatcherHarness>(() => new DispatcherHarness(configure));
    }

    internal DispatcherHarness H => _harness.Value;

    internal Task<string> Run(params string[] parts) => H.Run(parts);

    /// <summary>Builds the expected RESP text for an array of bulk strings.</summary>
    protected static string Bulk(string? value) =>
        value is null ? "$-1\r\n" : $"${System.Text.Encoding.UTF8.GetByteCount(value)}\r\n{value}\r\n";

    protected static string BulkArray(params string?[] values) =>
        $"*{values.Length}\r\n" + string.Concat(values.Select(Bulk));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_harness.IsValueCreated)
        {
            await _harness.Value.DisposeAsync();
        }
    }
}
