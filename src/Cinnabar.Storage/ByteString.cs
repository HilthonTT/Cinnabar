using System.Text;

namespace Cinnabar.Storage;

/// <summary>
/// Conversions for the server's binary-safe string representation.
/// </summary>
/// <remarks>
/// <para>
/// Redis keys and values are byte strings, not text. Cinnabar keeps them in ordinary
/// <see cref="string"/>s, but each <see cref="char"/> holds exactly one byte (0–255): the wire
/// bytes are decoded with <see cref="Encoding"/> (Latin-1), which maps every byte to the char
/// with the same value and back. Any payload, including invalid UTF-8, therefore survives
/// storage, replication and snapshots unchanged, and lengths and ranges count bytes, as in Redis.
/// </para>
/// <para>
/// A UTF-8 value such as <c>é</c> arrives as two chars (<c>0xC3 0xA9</c>). Text that originates
/// in the server itself (configuration, file paths) must go through <see cref="FromText"/>
/// before it is compared with or sent as client data.
/// </para>
/// </remarks>
public static class ByteString
{
    /// <summary>The byte-per-char encoding used on the wire and in RDB files.</summary>
    public static Encoding Encoding => Encoding.Latin1;

    /// <summary>Converts ordinary text to its byte-string form (its UTF-8 bytes, one per char).</summary>
    public static string FromText(string text) => Encoding.GetString(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>Interprets a byte string as UTF-8 text, for logs and messages.</summary>
    public static string ToText(string bytes) => System.Text.Encoding.UTF8.GetString(GetBytes(bytes));

    /// <summary>The bytes a byte string stands for.</summary>
    public static byte[] GetBytes(string bytes) => Encoding.GetBytes(bytes);

    /// <summary>The byte string for <paramref name="bytes"/>.</summary>
    public static string FromBytes(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes);
}
