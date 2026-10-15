namespace Cinnabar.Protocol;

/// <summary>
/// Thrown when bytes on the wire are not valid RESP, or exceed the configured limits.
/// The connection should be closed, because the stream can no longer be resynchronised.
/// </summary>
public sealed class RespProtocolException(string message) : Exception(message);
