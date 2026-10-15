using Cinnabar.Client;
using Cinnabar.Samples.WebApi.DTOs;

namespace Cinnabar.Samples.WebApi.Endpoints;

public static class KeyEndpoints
{
    public static RouteGroupBuilder MapKeyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/type/{key}", async (string key, RedisClient redis) =>
        {
            string type = await redis.TypeAsync(key);
            return Results.Ok(RedisResponse<string>.Ok(type));
        })
        .WithName("Type")
        .WithSummary("TYPE key — returns the data type stored at key")
        .WithTags(Tags.Keys);

        group.MapGet("/keys", async (string? pattern, RedisClient redis) =>
        {
            var keys = await redis.KeysAsync(pattern ?? "*");
            return Results.Ok(RedisResponse<List<string?>>.Ok(keys));
        })
        .WithName("Keys")
        .WithSummary("KEYS pattern — return keys matching a glob pattern")
        .WithTags(Tags.Keys);

        group.MapGet("/config/{parameter}", async (string parameter, RedisClient redis) =>
        {
            var values = await redis.ConfigGetAsync(parameter);
            return Results.Ok(RedisResponse<List<string?>>.Ok(values));
        })
        .WithName("ConfigGet")
        .WithSummary("CONFIG GET parameter — return server configuration value")
        .WithTags(Tags.Keys);

        return group;
    }
}
