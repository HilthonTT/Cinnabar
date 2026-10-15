using Cinnabar.Client;
using Cinnabar.Samples.WebApi.DTOs;

namespace Cinnabar.Samples.WebApi.Endpoints;

public static class ListEndpoints
{
    public static RouteGroupBuilder MapListEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/lpush", async (ListPushRequest request, RedisClient redis) =>
        {
            long count = await redis.LPushAsync(request.Key, request.Values);
            return Results.Ok(RedisResponse<long>.Ok(count));
        })
        .WithName("LPush")
        .WithSummary("LPUSH key value [value ...] — prepend to list head")
        .WithTags(Tags.List);

        group.MapPost("/rpush", async (ListPushRequest request, RedisClient redis) =>
        {
            long count = await redis.RPushAsync(request.Key, request.Values);
            return Results.Ok(RedisResponse<long>.Ok(count));
        })
        .WithName("RPush")
        .WithSummary("RPUSH key value [value ...] — append to list tail")
        .WithTags(Tags.List);

        group.MapPost("/lpop", async (LPopRequest request, RedisClient redis) =>
        {
            if (request.Count.HasValue)
            {
                var values = await redis.LPopAsync(request.Key, request.Count.Value);
                return Results.Ok(RedisResponse<List<string?>>.Ok(values));
            }

            string? value = await redis.LPopAsync(request.Key);
            return value is not null
                ? Results.Ok(RedisResponse<string>.Ok(value))
                : Results.Ok(RedisResponse<string>.Fail("List is empty or key not found (nil)"));
        })
        .WithName("LPop")
        .WithSummary("LPOP key [count] — remove and return elements from the head")
        .WithTags(Tags.List);

        group.MapPost("/blpop", async (BLPopRequest request, RedisClient redis, CancellationToken ct) =>
        {
            var result = await redis.BLPopAsync(request.Key, request.TimeoutSeconds, ct);

            return result.HasValue
                ? Results.Ok(RedisResponse<object>.Ok(new { result.Value.Key, result.Value.Value }))
                : Results.Ok(RedisResponse<object>.Fail("Timeout — no data arrived"));
        })
        .WithName("BLPop")
        .WithSummary("BLPOP key timeout — blocking left pop, waits for data")
        .WithTags(Tags.List);

        group.MapGet("/llen/{key}", async (string key, RedisClient redis) =>
        {
            long len = await redis.LLenAsync(key);
            return Results.Ok(RedisResponse<long>.Ok(len));
        })
        .WithName("LLen")
        .WithSummary("LLEN key — return list length")
        .WithTags(Tags.List);

        group.MapGet("/lrange/{key}", async (
            string key, int start, int end, RedisClient redis) =>
        {
            var values = await redis.LRangeAsync(key, start, end);
            return Results.Ok(RedisResponse<List<string?>>.Ok(values));
        })
        .WithName("LRange")
        .WithSummary("LRANGE key start end — return elements in range (inclusive)")
        .WithTags(Tags.List);

        return group;
    }
}
