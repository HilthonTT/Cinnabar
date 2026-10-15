using System.Reflection;
using Cinnabar.Server.Blocking;
using Cinnabar.Server.Commands;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Diagnostics;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Persistence;
using Cinnabar.Server.PubSub;
using Cinnabar.Server.Replication;
using Cinnabar.Server.Security;
using Cinnabar.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Cinnabar.Server.Hosting;

/// <summary>Registers the server's services.</summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds every Cinnabar server service, and every <see cref="ICommandHandler"/> in this
    /// assembly that carries a <see cref="CommandAttribute"/>.
    /// </summary>
    public static IServiceCollection AddCinnabarServer(this IServiceCollection services, ServerSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServerState>();
        services.AddSingleton<ServerMetrics>();

        services.AddSingleton(sp => new Keyspace(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<KeyNotifier>();
        services.AddSingleton<PubSubBroker>();
        services.AddSingleton<SnapshotService>();

        services.AddSingleton<ReplicationMaster>();
        services.AddSingleton<ReplicationClient>();

        services.AddSingleton<IpGuard>(sp => new IpGuard(settings, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ConnectionHandler>();
        services.AddSingleton<TcpServer>();

        services.AddSingleton<CommandDispatcher>();

        // EXEC and COMMAND need the dispatcher, which needs every handler: break the cycle lazily.
        services.AddSingleton(sp => new Lazy<CommandDispatcher>(sp.GetRequiredService<CommandDispatcher>));

        foreach (Type handler in DiscoverHandlers())
        {
            services.AddSingleton(typeof(ICommandHandler), handler);
        }

        return services;
    }

    /// <summary>Concrete handler types declared in this assembly.</summary>
    internal static IEnumerable<Type> DiscoverHandlers() =>
        typeof(ICommandHandler).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        typeof(ICommandHandler).IsAssignableFrom(t) &&
                        t.GetCustomAttribute<CommandAttribute>() is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
}
