using System.Globalization;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands;

/// <summary>
/// Argument parsing helpers. Failures throw <see cref="StorageException"/>, which the
/// dispatcher turns into the matching Redis error reply.
/// </summary>
internal static class Arguments
{
    public const string NotAnInteger = "ERR value is not an integer or out of range";
    public const string SyntaxError = "ERR syntax error";

    /// <summary>Case-insensitive comparison for option keywords such as <c>EX</c> or <c>COUNT</c>.</summary>
    public static bool Is(string argument, string keyword) =>
        string.Equals(argument, keyword, StringComparison.OrdinalIgnoreCase);

    public static long ParseLong(string text) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
            ? value
            : throw new StorageException(NotAnInteger);

    public static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new StorageException(NotAnInteger);

    /// <summary>Parses a non-negative count (LPOP count, XRANGE COUNT, ...).</summary>
    public static int ParseCount(string text)
    {
        int value = ParseInt(text);
        return value >= 0 ? value : throw new StorageException("ERR value is out of range, must be positive");
    }

    /// <summary>Parses a blocking timeout in seconds; decimals are allowed (BLPOP).</summary>
    public static TimeSpan? ParseTimeoutSeconds(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ||
            double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            throw new StorageException("ERR timeout is not a float or out of range");
        }

        if (seconds < 0)
        {
            throw new StorageException("ERR timeout is negative");
        }

        // 0 means "block forever".
        return seconds == 0 ? null : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Parses a blocking timeout in milliseconds (XREAD BLOCK, WAIT).</summary>
    public static TimeSpan? ParseTimeoutMilliseconds(string text)
    {
        long ms = ParseLong(text);
        if (ms < 0)
        {
            throw new StorageException("ERR timeout is negative");
        }

        return ms == 0 ? null : TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>Formats a number for the wire.</summary>
    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
