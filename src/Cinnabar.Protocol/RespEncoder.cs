using System.Buffers;
using System.Globalization;
using System.Text;

namespace Cinnabar.Protocol;

/// <summary>
/// Encodes values into RESP2 wire bytes. Every method returns a new array, so the
/// results can be cached and shared between threads.
/// </summary>
public static class RespEncoder
{
    /// <summary><c>+OK\r\n</c></summary>
    public static ReadOnlySpan<byte> Ok => "+OK\r\n"u8;

    /// <summary><c>$-1\r\n</c></summary>
    public static ReadOnlySpan<byte> NullBulkString => "$-1\r\n"u8;

    /// <summary><c>*-1\r\n</c></summary>
    public static ReadOnlySpan<byte> NullArray => "*-1\r\n"u8;

    /// <summary><c>*0\r\n</c></summary>
    public static ReadOnlySpan<byte> EmptyArray => "*0\r\n"u8;

    /// <summary>Encodes a simple string. CR and LF are not allowed in simple strings and are replaced by spaces.</summary>
    public static byte[] SimpleString(string text) => Line('+', text);

    /// <summary>Encodes an error reply such as <c>ERR syntax error</c>. CR and LF are replaced by spaces.</summary>
    public static byte[] Error(string message) => Line('-', message);

    /// <summary>Encodes an integer.</summary>
    public static byte[] Integer(long value) =>
        Encoding.ASCII.GetBytes($":{value.ToString(CultureInfo.InvariantCulture)}\r\n");

    /// <summary>Encodes a bulk string, or the null bulk string when <paramref name="value"/> is <see langword="null"/>.</summary>
    public static byte[] BulkString(string? value)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteBulkString(writer, value);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Encodes raw bytes as a bulk string.</summary>
    public static byte[] BulkString(ReadOnlySpan<byte> value)
    {
        var writer = new ArrayBufferWriter<byte>(value.Length + 16);
        WriteHeader(writer, '$', value.Length);
        writer.Write(value);
        writer.Write("\r\n"u8);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Encodes an array of bulk strings; <see langword="null"/> elements become null bulk strings.</summary>
    public static byte[] BulkStringArray(IEnumerable<string?> values)
    {
        IReadOnlyCollection<string?> items = values as IReadOnlyCollection<string?> ?? values.ToList();
        var writer = new ArrayBufferWriter<byte>();
        WriteHeader(writer, '*', items.Count);
        foreach (string? item in items)
        {
            WriteBulkString(writer, item);
        }

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Encodes an array whose elements are already encoded.</summary>
    public static byte[] Array(IReadOnlyCollection<byte[]> encodedElements)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteHeader(writer, '*', encodedElements.Count);
        foreach (byte[] element in encodedElements)
        {
            writer.Write(element);
        }

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Encodes a command (name and arguments) the way clients send it: an array of bulk strings.
    /// </summary>
    public static byte[] Command(params IEnumerable<string> parts) => BulkStringArray(parts);

    /// <summary>Encodes <paramref name="value"/> back to its wire form.</summary>
    public static byte[] Encode(RespValue value) => value.Type switch
    {
        RespType.SimpleString => SimpleString(value.Text ?? ""),
        RespType.Error => Error(value.Text ?? ""),
        RespType.Integer => Integer(value.IntegerValue),
        RespType.BulkString => BulkString(value.Text),
        RespType.Null => NullBulkString.ToArray(),
        RespType.NullArray => NullArray.ToArray(),
        RespType.Array => Array(value.AsArray().Select(Encode).ToList()),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static void WriteBulkString(ArrayBufferWriter<byte> writer, string? value)
    {
        if (value is null)
        {
            writer.Write(NullBulkString);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteHeader(writer, '$', byteCount);
        int written = Encoding.UTF8.GetBytes(value, writer.GetSpan(byteCount));
        writer.Advance(written);
        writer.Write("\r\n"u8);
    }

    private static void WriteHeader(ArrayBufferWriter<byte> writer, char prefix, long length)
    {
        string header = $"{prefix}{length.ToString(CultureInfo.InvariantCulture)}\r\n";
        int written = Encoding.ASCII.GetBytes(header, writer.GetSpan(header.Length));
        writer.Advance(written);
    }

    private static byte[] Line(char prefix, string text)
    {
        string sanitized = text.Replace('\r', ' ').Replace('\n', ' ');
        return Encoding.UTF8.GetBytes($"{prefix}{sanitized}\r\n");
    }
}
