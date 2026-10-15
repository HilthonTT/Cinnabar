using Cinnabar.Client;
using Cinnabar.Samples.WebApi.DTOs;

namespace Cinnabar.Samples.WebApi.Endpoints;

public static class StringEndpoints
{
    public static RouteGroupBuilder MapStringEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/ping", async (RedisClient redis) =>
        {
            bool pong = await redis.PingAsync();
            return Results.Ok(RedisResponse<string>.Ok(pong ? "PONG" : "NO RESPONSE"));
        })
        .WithName("Ping")
        .WithSummary("PING — verify server is alive")
        .WithTags(Tags.String);

        group.MapGet("/echo/{message}", async (string message, RedisClient redis) =>
        {
            string? result = await redis.EchoAsync(message);
            return Results.Ok(RedisResponse<string>.Ok(result!));
        })
        .WithName("Echo")
        .WithSummary("ECHO message — returns the same message back")
        .WithTags(Tags.String);

        group.MapGet("/get/{key}", async (string key, RedisClient redis) =>
        {
            string? value = await redis.GetAsync(key);
            return value is not null
                ? Results.Ok(RedisResponse<string>.Ok(value))
                : Results.Ok(RedisResponse<string>.Fail("Key not found (nil)"));
        })
        .WithName("Get")
        .WithSummary("GET key — retrieve a string value")
        .WithTags(Tags.String);

        group.MapPost("/set", async (SetRequest request, RedisClient redis) =>
        {
            if (request.ExpiryMs.HasValue)
            {
                await redis.SetAsync(request.Key, request.Value,
                    TimeSpan.FromMilliseconds(request.ExpiryMs.Value));
            }
            else
            {
                await redis.SetAsync(request.Key, request.Value);
            }

            return Results.Ok(RedisResponse<string>.Ok("OK"));
        })
        .WithName("Set")
        .WithSummary("SET key value [PX ms] — store a string, optionally with expiry")
        .WithTags(Tags.String);

        return group;
    }
}
