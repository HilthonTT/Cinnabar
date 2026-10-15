using System.Text;
using Cinnabar.Protocol;

namespace Cinnabar.Server.Commands;

/// <summary>
/// A pre-encoded response to a command, plus instructions for the connection and replication.
/// </summary>
internal sealed class Reply
{
    private Reply(ReadOnlyMemory<byte> bytes, bool isError)
    {
        Bytes = bytes;
        IsError = isError;
    }

    /// <summary><c>+OK</c></summary>
    public static Reply Ok { get; } = new(RespEncoder.Ok.ToArray(), false);

    /// <summary><c>+QUEUED</c>, the response to a command sent inside MULTI.</summary>
    public static Reply Queued { get; } = SimpleString("QUEUED");

    /// <summary><c>$-1</c>, the null bulk string.</summary>
    public static Reply NullBulkString { get; } = new(RespEncoder.NullBulkString.ToArray(), false);

    /// <summary><c>*-1</c>, the null array.</summary>
    public static Reply NullArray { get; } = new(RespEncoder.NullArray.ToArray(), false);

    /// <summary><c>*0</c>, the empty array.</summary>
    public static Reply EmptyArray { get; } = new(RespEncoder.EmptyArray.ToArray(), false);

    /// <summary><c>:0</c></summary>
    public static Reply Zero { get; } = Integer(0);

    /// <summary><c>:1</c></summary>
    public static Reply One { get; } = Integer(1);

    /// <summary>Send nothing (used for <c>REPLCONF ACK</c> and after <c>PSYNC</c>).</summary>
    public static Reply None { get; } = new(ReadOnlyMemory<byte>.Empty, false);

    /// <summary>Encoded bytes to send; empty for <see cref="None"/>.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary><see langword="true"/> for error replies. Errors are never propagated to replicas.</summary>
    public bool IsError { get; }

    /// <summary>Close the connection after sending this reply (QUIT).</summary>
    public bool CloseConnection { get; private init; }

    /// <summary>
    /// What to send to replicas instead of the original command, for commands whose effect
    /// depends on the master (for example <c>XADD *</c> is replicated with the generated ID).
    /// </summary>
    public Command? PropagateAs { get; private init; }

    /// <summary>Do not propagate this write; the handler already did (blocking commands).</summary>
    public bool SkipPropagation { get; private init; }

    /// <summary><see langword="true"/> for <see cref="None"/>.</summary>
    public bool IsEmpty => Bytes.IsEmpty;

    /// <summary>Creates an error reply. Prefix it with an error code such as <c>ERR</c> or <c>WRONGTYPE</c>.</summary>
    public static Reply Error(string message) => new(RespEncoder.Error(message), true);

    /// <summary>Creates a simple string reply.</summary>
    public static Reply SimpleString(string text) => new(RespEncoder.SimpleString(text), false);

    /// <summary>Creates a bulk string reply, or <see cref="NullBulkString"/> for <see langword="null"/>.</summary>
    public static Reply BulkString(string? text) => text is null ? NullBulkString : new(RespEncoder.BulkString(text), false);

    /// <summary>Creates an integer reply.</summary>
    public static Reply Integer(long value) => new(RespEncoder.Integer(value), false);

    /// <summary>Creates an array of bulk strings.</summary>
    public static Reply BulkStringArray(IEnumerable<string?> values) => new(RespEncoder.BulkStringArray(values), false);

    /// <summary>Creates an array whose elements are other replies.</summary>
    public static Reply Array(IEnumerable<Reply> elements) =>
        new(RespEncoder.Array(elements.Select(e => e.Bytes.ToArray()).ToList()), false);

    /// <summary>Sends several replies back to back (for example one confirmation per SUBSCRIBE channel).</summary>
    public static Reply Sequence(IEnumerable<Reply> replies) =>
        new(replies.SelectMany(r => r.Bytes.ToArray()).ToArray(), false);

    /// <summary>Wraps bytes that are already RESP-encoded.</summary>
    public static Reply Raw(byte[] encoded) => new(encoded, false);

    /// <summary>Returns a copy that replicates as <paramref name="command"/>.</summary>
    public Reply WithPropagation(Command command) => new(Bytes, IsError)
    {
        CloseConnection = CloseConnection,
        PropagateAs = command,
    };

    /// <summary>Returns a copy that is not propagated by the dispatcher.</summary>
    public Reply WithoutPropagation() => new(Bytes, IsError)
    {
        CloseConnection = CloseConnection,
        SkipPropagation = true,
    };

    /// <summary>Returns a copy that closes the connection once sent.</summary>
    public Reply ThenClose() => new(Bytes, IsError) { CloseConnection = true };

    /// <summary>The wire bytes as text, for logs and tests.</summary>
    public override string ToString() => Encoding.UTF8.GetString(Bytes.Span);
}
