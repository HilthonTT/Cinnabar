namespace Cinnabar.Protocol;

/// <summary>
/// The RESP2 value kinds, identified on the wire by their first byte.
/// </summary>
public enum RespType
{
    /// <summary><c>+OK\r\n</c>: a short, non-binary-safe status string.</summary>
    SimpleString,

    /// <summary><c>-ERR message\r\n</c>: an error reply.</summary>
    Error,

    /// <summary><c>:42\r\n</c>: a signed 64-bit integer.</summary>
    Integer,

    /// <summary><c>$5\r\nhello\r\n</c>: a length-prefixed, binary-safe string.</summary>
    BulkString,

    /// <summary><c>*2\r\n...</c>: an ordered list of nested values.</summary>
    Array,

    /// <summary><c>$-1\r\n</c>: the null bulk string (for example, GET on a missing key).</summary>
    Null,

    /// <summary><c>*-1\r\n</c>: the null array (for example, BLPOP after a timeout).</summary>
    NullArray,
}
