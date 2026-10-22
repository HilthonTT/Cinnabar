using System.Globalization;

namespace Cinnabar.Protocol;

/// <summary>
/// An immutable, decoded RESP2 value.
/// </summary>
/// <remarks>
/// Strings are decoded with <see cref="RespReaderOptions.TextEncoding"/>: UTF-8 by default,
/// or one <see cref="char"/> per byte on the server, so binary payloads round-trip exactly.
/// </remarks>
public sealed class RespValue
{
    private static readonly RespValue NullInstance = new(RespType.Null);
    private static readonly RespValue NullArrayInstance = new(RespType.NullArray);

    private RespValue(RespType type, string? text = null, long integer = 0, RespValue[]? elements = null)
    {
        Type = type;
        Text = text;
        IntegerValue = integer;
        Elements = elements;
    }

    /// <summary>The kind of value.</summary>
    public RespType Type { get; }

    /// <summary>The payload of a simple string, error or bulk string; otherwise <see langword="null"/>.</summary>
    public string? Text { get; }

    /// <summary>The payload of an integer; otherwise 0.</summary>
    public long IntegerValue { get; }

    /// <summary>The elements of an array; otherwise <see langword="null"/>.</summary>
    public IReadOnlyList<RespValue>? Elements { get; }

    /// <summary><see langword="true"/> for the null bulk string and the null array.</summary>
    public bool IsNull => Type is RespType.Null or RespType.NullArray;

    /// <summary><see langword="true"/> when the value is an error reply.</summary>
    public bool IsError => Type == RespType.Error;

    /// <summary>Creates a simple string.</summary>
    public static RespValue SimpleString(string text) => new(RespType.SimpleString, text: text);

    /// <summary>Creates an error reply.</summary>
    public static RespValue Error(string text) => new(RespType.Error, text: text);

    /// <summary>Creates an integer.</summary>
    public static RespValue Integer(long value) => new(RespType.Integer, integer: value);

    /// <summary>Creates a bulk string.</summary>
    public static RespValue BulkString(string text) => new(RespType.BulkString, text: text);

    /// <summary>Creates an array. The array is not copied, so do not mutate it afterwards.</summary>
    public static RespValue Array(RespValue[] elements) => new(RespType.Array, elements: elements);

    /// <summary>The null bulk string.</summary>
    public static RespValue Null() => NullInstance;

    /// <summary>The null array.</summary>
    public static RespValue NullArray() => NullArrayInstance;

    /// <summary>
    /// Returns the value as text: the payload for strings and errors, the decimal form of an
    /// integer, and <see langword="null"/> for nulls and arrays.
    /// </summary>
    public string? AsString() => Type switch
    {
        RespType.SimpleString or RespType.BulkString or RespType.Error => Text,
        RespType.Integer => IntegerValue.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>Returns the integer payload.</summary>
    /// <exception cref="InvalidOperationException">The value is not an integer.</exception>
    public long AsLong() => Type == RespType.Integer
        ? IntegerValue
        : throw new InvalidOperationException($"Cannot convert a RESP {Type} to an integer.");

    /// <summary>Returns the array elements; a null array yields an empty list.</summary>
    /// <exception cref="InvalidOperationException">The value is neither an array nor a null array.</exception>
    public IReadOnlyList<RespValue> AsArray() => Type switch
    {
        RespType.Array => Elements ?? [],
        RespType.NullArray => [],
        _ => throw new InvalidOperationException($"Cannot convert a RESP {Type} to an array."),
    };

    /// <summary>Returns the array elements converted with <see cref="AsString"/>.</summary>
    /// <exception cref="InvalidOperationException">The value is neither an array nor a null array.</exception>
    public List<string?> AsStringList()
    {
        IReadOnlyList<RespValue> elements = AsArray();
        var result = new List<string?>(elements.Count);
        foreach (RespValue element in elements)
        {
            result.Add(element.AsString());
        }

        return result;
    }

    /// <summary>Returns a short, human-readable description (not the wire format).</summary>
    public override string ToString() => Type switch
    {
        RespType.SimpleString => $"+{Text}",
        RespType.Error => $"-{Text}",
        RespType.Integer => $":{IntegerValue}",
        RespType.BulkString => $"\"{Text}\"",
        RespType.Null => "(nil)",
        RespType.NullArray => "(nil array)",
        RespType.Array => $"[{string.Join(", ", Elements ?? [])}]",
        _ => "(unknown)",
    };
}
