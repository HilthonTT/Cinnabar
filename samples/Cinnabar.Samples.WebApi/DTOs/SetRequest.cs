namespace Cinnabar.Samples.WebApi.DTOs;

public sealed record SetRequest(string Key, string Value, long? ExpiryMs = null);