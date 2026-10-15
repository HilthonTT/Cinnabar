namespace Cinnabar.Client;

/// <summary>
/// The client could not talk to the server: connecting failed or timed out, the pool was
/// exhausted, the connection dropped, or the server sent bytes that are not valid RESP.
/// </summary>
public sealed class RedisConnectionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
