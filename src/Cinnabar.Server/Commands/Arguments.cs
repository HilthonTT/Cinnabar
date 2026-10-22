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

    /// <summary>Formats a floating-point number (a score) for the wire.</summary>
    public static string Format(double value) => DoubleText.Format(value);

    public const string NotAFloat = "ERR value is not a valid float";

    /// <summary>Parses a float such as a score or an increment; <c>inf</c> is allowed, NaN is not.</summary>
    public static double ParseDouble(string text) =>
        DoubleText.TryParse(text, out double value) ? value : throw new StorageException(NotAFloat);

    /// <summary>Parses a score-range end: <c>1.5</c>, <c>(1.5</c>, <c>-inf</c>, <c>+inf</c>.</summary>
    public static ScoreBound ParseScoreBound(string text) =>
        ScoreBound.TryParse(text, out ScoreBound bound) ? bound : throw new StorageException("ERR min or max is not a float");

    /// <summary>Parses a lexicographic range end: <c>[a</c>, <c>(a</c>, <c>-</c>, <c>+</c>.</summary>
    public static LexBound ParseLexBound(string text) =>
        LexBound.TryParse(text, out LexBound bound) ? bound : throw new StorageException("ERR min or max not valid string range item");

    /// <summary>
    /// Reads <c>name value</c> pairs from <paramref name="start"/> to the end of the arguments.
    /// The caller has checked that the count is even.
    /// </summary>
    public static List<KeyValuePair<string, string>> Pairs(Command command, int start)
    {
        var pairs = new List<KeyValuePair<string, string>>((command.Count - start) / 2);
        for (int i = start; i + 1 < command.Count; i += 2)
        {
            pairs.Add(new KeyValuePair<string, string>(command[i], command[i + 1]));
        }

        return pairs;
    }

    /// <summary>The error for a command whose argument count has the wrong shape.</summary>
    public static StorageException WrongArity(Command command) =>
        new($"ERR wrong number of arguments for '{command.Name.ToLowerInvariant()}' command");
}
