namespace Cinnabar.Samples.WebApi.DTOs;

public sealed record XAddRequest(string StreamKey, string? Id = "*", Dictionary<string, string>? Fields = null);
