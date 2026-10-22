using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Text;

namespace Cinnabar.Protocol;

/// <summary>
/// Incremental RESP2 parser. It works on <see cref="ReadOnlySequence{T}"/>, so it copes
/// with values split across TCP reads and with several pipelined values in one read.
/// </summary>
public static class RespReader
{
    private static readonly char[] InlineSeparators = [' ', '\t', '\r', '\n', '\v', '\f'];

    private static ReadOnlySpan<byte> CrLf => "\r\n"u8;

    /// <summary>
    /// Reads the next complete value from <paramref name="reader"/>.
    /// </summary>
    /// <returns>The value, or <see langword="null"/> when the peer closed the connection cleanly between values.</returns>
    /// <exception cref="EndOfStreamException">The connection closed in the middle of a value.</exception>
    /// <exception cref="RespProtocolException">The peer sent malformed RESP.</exception>
    public static async ValueTask<RespValue?> ReadAsync(
        PipeReader reader,
        RespReaderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RespReaderOptions.Default;

        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            bool parsed;
            RespValue? value;
            try
            {
                parsed = TryRead(ref buffer, out value, options);
            }
            catch
            {
                reader.AdvanceTo(buffer.Start);
                throw;
            }

            if (parsed)
            {
                reader.AdvanceTo(buffer.Start);
                return value;
            }

            // Nothing consumed; everything examined. Wait for more bytes.
            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                if (buffer.IsEmpty)
                {
                    return null;
                }

                throw new EndOfStreamException("Connection closed in the middle of a RESP value.");
            }

            if (result.IsCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Tries to parse one complete value from the start of <paramref name="buffer"/>.
    /// On success, <paramref name="buffer"/> is advanced past the value; otherwise it is left untouched.
    /// </summary>
    /// <returns><see langword="false"/> when more bytes are needed.</returns>
    /// <exception cref="RespProtocolException">The bytes are not valid RESP.</exception>
    public static bool TryRead(ref ReadOnlySequence<byte> buffer, out RespValue? value, RespReaderOptions? options = null)
    {
        options ??= RespReaderOptions.Default;

        var reader = new SequenceReader<byte>(buffer);
        if (!TryReadValue(ref reader, options, depth: 0, out value))
        {
            return false;
        }

        buffer = buffer.Slice(reader.Position);
        return true;
    }

    private static bool TryReadValue(ref SequenceReader<byte> reader, RespReaderOptions options, int depth, out RespValue? value)
    {
        value = null;
        if (!reader.TryPeek(out byte prefix))
        {
            return false;
        }

        switch (prefix)
        {
            case (byte)'+':
                return TryReadLineValue(ref reader, options, text => RespValue.SimpleString(text), out value);

            case (byte)'-':
                return TryReadLineValue(ref reader, options, text => RespValue.Error(text), out value);

            case (byte)':':
                if (!TryReadNumberLine(ref reader, options, out long integer))
                {
                    return false;
                }

                value = RespValue.Integer(integer);
                return true;

            case (byte)'$':
                return TryReadBulkString(ref reader, options, out value);

            case (byte)'*':
                return TryReadArray(ref reader, options, depth, out value);

            default:
                if (depth == 0 && options.AllowInlineCommands)
                {
                    return TryReadInline(ref reader, options, out value);
                }

                throw new RespProtocolException(
                    $"Unexpected RESP type byte 0x{prefix:X2} ('{(char)prefix}').");
        }
    }

    private static bool TryReadLineValue(
        ref SequenceReader<byte> reader,
        RespReaderOptions options,
        Func<string, RespValue> create,
        out RespValue? value)
    {
        value = null;
        if (!TryReadLine(ref reader, options, out ReadOnlySequence<byte> line))
        {
            return false;
        }

        value = create(Decode(line.Slice(1), options));
        return true;
    }

    private static bool TryReadBulkString(ref SequenceReader<byte> reader, RespReaderOptions options, out RespValue? value)
    {
        value = null;
        SequenceReader<byte> start = reader;

        if (!TryReadNumberLine(ref reader, options, out long length))
        {
            return false;
        }

        if (length < 0)
        {
            value = RespValue.Null();
            return true;
        }

        if (length > options.MaxBulkLength)
        {
            throw new RespProtocolException($"Bulk string length {length} exceeds the limit of {options.MaxBulkLength}.");
        }

        if (reader.Remaining < length + CrLf.Length)
        {
            reader = start;
            return false;
        }

        ReadOnlySequence<byte> payload = reader.UnreadSequence.Slice(0, length);
        reader.Advance(length);

        if (!reader.IsNext(CrLf, advancePast: true))
        {
            throw new RespProtocolException("Bulk string is not terminated by CRLF.");
        }

        value = RespValue.BulkString(Decode(payload, options));
        return true;
    }

    private static bool TryReadArray(ref SequenceReader<byte> reader, RespReaderOptions options, int depth, out RespValue? value)
    {
        value = null;
        SequenceReader<byte> start = reader;

        if (depth >= options.MaxDepth)
        {
            throw new RespProtocolException($"Array nesting exceeds the limit of {options.MaxDepth}.");
        }

        if (!TryReadNumberLine(ref reader, options, out long count))
        {
            return false;
        }

        if (count < 0)
        {
            value = RespValue.NullArray();
            return true;
        }

        if (count > options.MaxArrayLength)
        {
            throw new RespProtocolException($"Array length {count} exceeds the limit of {options.MaxArrayLength}.");
        }

        // Each element needs at least 3 bytes ("+\r\n"); don't allocate for elements that can't be here yet.
        if (reader.Remaining < count * 3)
        {
            reader = start;
            return false;
        }

        var elements = new RespValue[count];
        for (int i = 0; i < count; i++)
        {
            if (!TryReadValue(ref reader, options, depth + 1, out RespValue? element))
            {
                reader = start;
                return false;
            }

            elements[i] = element!;
        }

        value = RespValue.Array(elements);
        return true;
    }

    private static bool TryReadInline(ref SequenceReader<byte> reader, RespReaderOptions options, out RespValue? value)
    {
        value = null;
        if (!TryReadLine(ref reader, options, out ReadOnlySequence<byte> line))
        {
            return false;
        }

        // Split on ASCII whitespace only: with a byte-exact encoding, chars such as U+00A0 are
        // bytes inside a multi-byte UTF-8 sequence, not separators.
        string[] parts = Decode(line, options).Split(InlineSeparators, StringSplitOptions.RemoveEmptyEntries);
        value = RespValue.Array(Array.ConvertAll(parts, RespValue.BulkString));
        return true;
    }

    private static bool TryReadNumberLine(ref SequenceReader<byte> reader, RespReaderOptions options, out long number)
    {
        number = 0;
        if (!TryReadLine(ref reader, options, out ReadOnlySequence<byte> line))
        {
            return false;
        }

        ReadOnlySequence<byte> digits = line.Slice(1);
        Span<byte> scratch = stackalloc byte[24];
        if (digits.Length > scratch.Length)
        {
            throw new RespProtocolException("Integer header is too long.");
        }

        digits.CopyTo(scratch);
        ReadOnlySpan<byte> span = scratch[..(int)digits.Length];
        if (!Utf8Parser.TryParse(span, out number, out int consumed) || consumed != span.Length)
        {
            throw new RespProtocolException($"Invalid integer '{Encoding.ASCII.GetString(span)}'.");
        }

        return true;
    }

    private static bool TryReadLine(ref SequenceReader<byte> reader, RespReaderOptions options, out ReadOnlySequence<byte> line)
    {
        if (reader.TryReadTo(out line, CrLf, advancePastDelimiter: true))
        {
            if (line.Length > options.MaxLineLength)
            {
                throw new RespProtocolException($"Line exceeds the limit of {options.MaxLineLength} bytes.");
            }

            return true;
        }

        if (reader.Remaining > options.MaxLineLength)
        {
            throw new RespProtocolException($"Line exceeds the limit of {options.MaxLineLength} bytes.");
        }

        return false;
    }

    private static string Decode(ReadOnlySequence<byte> bytes, RespReaderOptions options) =>
        bytes.IsSingleSegment
            ? options.TextEncoding.GetString(bytes.FirstSpan)
            : options.TextEncoding.GetString(in bytes);
}
