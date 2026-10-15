namespace Cinnabar.Client;

/// <summary>
/// The server answered with an error reply, for example <c>WRONGTYPE ...</c> or
/// <c>ERR value is not an integer or out of range</c>. The connection is still usable.
/// </summary>
public sealed class RedisException(string message) : Exception(message)
{
    /// <summary>The error code: the first word of the reply, such as <c>ERR</c>, <c>WRONGTYPE</c> or <c>NOAUTH</c>.</summary>
    public string ErrorCode { get; } = message.Split(' ', 2)[0];
}
