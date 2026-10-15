using Microsoft.Extensions.DependencyInjection;

namespace Cinnabar.Client.DependencyInjection;

/// <summary>Registers <see cref="RedisClient"/> with Microsoft.Extensions.DependencyInjection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="RedisClient"/> configured by <paramref name="configure"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisClient(options =>
    /// {
    ///     options.Host = "localhost";
    ///     options.Port = 6379;
    ///     options.PoolSize = 20;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddRedisClient(this IServiceCollection services, Action<RedisClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RedisClientOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(sp => new RedisClient(sp.GetRequiredService<RedisClientOptions>()));
        return services;
    }

    /// <summary>Registers a singleton <see cref="RedisClient"/> for <c>localhost:6379</c>.</summary>
    public static IServiceCollection AddRedisClient(this IServiceCollection services) =>
        services.AddRedisClient(_ => { });

    /// <summary>
    /// Registers a singleton <see cref="RedisClient"/> from a connection string such as
    /// <c>localhost:6379</c> or <c>redis://user:password@host:6379</c>.
    /// </summary>
    public static IServiceCollection AddRedisClient(this IServiceCollection services, string connectionString) =>
        services.AddRedisClient(options => options.ConnectionString = connectionString);
}
