namespace Cinnabar.Samples.WebApi.DTOs;

public sealed record RedisResponse<T>(bool Success, T? Data, string? Error = null)
{
    public static RedisResponse<T> Ok(T data) => new(true, data);
    public static RedisResponse<T> Fail(string error) => new(false, default, error);
}
