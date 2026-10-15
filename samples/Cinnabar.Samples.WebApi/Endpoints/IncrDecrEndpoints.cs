using Cinnabar.Client;
using Cinnabar.Samples.WebApi.DTOs;

namespace Cinnabar.Samples.WebApi.Endpoints;

public static class IncrDecrEndpoints
{
    private sealed record IncrByRequest(long Delta);
    private sealed record DecrByRequest(long Delta);

    public static RouteGroupBuilder MapIncrDecrEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/incr/{key}", async (string key, RedisClient redis) =>
        {
            long newValue = await redis.IncrementAsync(key);
            return Results.Ok(RedisResponse<long>.Ok(newValue));
        })
        .WithName("Incr")
        .WithSummary("ICR command to increment the integer value of a key by one.")
        .WithTags(Tags.IncrDecr);

        group.MapPost("/incr-by/{key}", async (string key, IncrByRequest request, RedisClient redis) =>
        {
            long newValue = await redis.IncrementByAsync(key, request.Delta);
            return Results.Ok(RedisResponse<long>.Ok(newValue));
        })
        .WithName("IncrBy")
        .WithSummary("ICRBY By command to increment the integer value of a key by provided delta.")
        .WithTags(Tags.IncrDecr);

        group.MapPost("decr/{key}", async (string key, RedisClient redis) =>
        {
            long newValue = await redis.DecrementAsync(key);
            return Results.Ok(RedisResponse<long>.Ok(newValue));
        })
        .WithName("Decr")
        .WithSummary("DECR command to decrement the integer value of a key by one.")
        .WithTags(Tags.IncrDecr);

        group.MapPost("/decr-by/{key}", async (string key, DecrByRequest request, RedisClient redis) =>
        {
            long newValue = await redis.DecrementByAsync(key, request.Delta);
            return Results.Ok(RedisResponse<long>.Ok(newValue));
        })
       .WithName("DecrBy")
       .WithSummary("DECRBY By command to decrement the integer value of a key by provided delta.")
       .WithTags(Tags.IncrDecr);

        return group;
    }
}
