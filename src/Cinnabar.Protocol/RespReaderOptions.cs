using System.Text;

namespace Cinnabar.Protocol;

/// <summary>
/// Limits and switches for <see cref="RespReader"/>. The limits protect a server from
/// peers that announce huge lengths or never terminate a line.
/// </summary>
public sealed record RespReaderOptions
{
    /// <summary>Defaults suitable for reading replies from a trusted server.</summary>
    public static readonly RespReaderOptions Default = new();

    /// <summary>
    /// Defaults for a server reading commands from untrusted clients: inline commands are
    /// allowed, and strings are decoded byte-for-byte (see <see cref="TextEncoding"/>).
    /// </summary>
    public static readonly RespReaderOptions Server = new()
    {
        AllowInlineCommands = true,
        TextEncoding = Encoding.Latin1,
    };

    /// <summary>
    /// Like <see cref="Default"/>, but decodes strings byte-for-byte (see <see cref="TextEncoding"/>).
    /// </summary>
    public static readonly RespReaderOptions ByteExact = new() { TextEncoding = Encoding.Latin1 };

    /// <summary>
    /// How payload bytes become .NET strings. UTF-8 (the default) suits clients that deal in
    /// text. <see cref="Encoding.Latin1"/> maps every byte to exactly one <see cref="char"/>
    /// (0–255) and back, so arbitrary binary payloads survive a round trip unchanged; pair it
    /// with the same encoding in <see cref="RespEncoder"/>.
    /// </summary>
    public Encoding TextEncoding { get; init; } = Encoding.UTF8;

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
