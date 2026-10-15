namespace Cinnabar.Protocol;

/// <summary>
/// Limits and switches for <see cref="RespReader"/>. The limits protect a server from
/// peers that announce huge lengths or never terminate a line.
/// </summary>
public sealed record RespReaderOptions
{
    /// <summary>Defaults suitable for reading replies from a trusted server.</summary>
    public static readonly RespReaderOptions Default = new();

    /// <summary>Defaults for a server reading commands from untrusted clients.</summary>
    public static readonly RespReaderOptions Server = new() { AllowInlineCommands = true };

    /// <summary>
    /// Accept "inline" commands such as <c>PING\r\n</c> typed into telnet/netcat. An inline
    /// command is returned as an array of bulk strings, split on whitespace.
    /// </summary>
    public bool AllowInlineCommands { get; init; }

    /// <summary>Largest accepted bulk string, in bytes. Redis uses 512 MB.</summary>
    public int MaxBulkLength { get; init; } = 512 * 1024 * 1024;

    /// <summary>Largest accepted array element count.</summary>
    public int MaxArrayLength { get; init; } = 1024 * 1024;

    /// <summary>Longest accepted header or inline line, in bytes.</summary>
    public int MaxLineLength { get; init; } = 64 * 1024;

    /// <summary>Deepest accepted array nesting.</summary>
    public int MaxDepth { get; init; } = 32;
}
