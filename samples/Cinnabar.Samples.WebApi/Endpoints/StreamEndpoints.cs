using Cinnabar.Client;
using Cinnabar.Samples.WebApi.DTOs;

namespace Cinnabar.Samples.WebApi.Endpoints;


public static class StreamEndpoints
{
    public static RouteGroupBuilder MapStreamEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/xadd", async (XAddRequest request, RedisClient redis) =>
        {
            var fields = request.Fields ?? new Dictionary<string, string>();
            string id = request.Id ?? "*";

            string? entryId = await redis.XAddAsync(request.StreamKey, id, fields);

            return Results.Ok(RedisResponse<string>.Ok(entryId!));
        })
        .WithName("XAdd")
        .WithSummary("XADD stream id field value [...] — append entry to stream")
        .WithTags(Tags.Stream);

        return group;
    }
}
