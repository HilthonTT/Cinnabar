using Cinnabar.Protocol;

namespace Cinnabar.Client;

/// <summary>Helpers for working with replies returned by <see cref="RedisClient.ExecuteAsync(string[])"/>.</summary>
public static class RespValueExtensions
{
    /// <summary>Throws <see cref="RedisException"/> if <paramref name="value"/> is an error reply; otherwise returns it.</summary>
    public static RespValue ThrowIfError(this RespValue value) =>
        value.IsError ? throw new RedisException(value.Text ?? "Unknown error") : value;
}
