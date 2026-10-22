using System.Globalization;

namespace Cinnabar.Storage;

/// <summary>Parses and formats floating-point numbers the way Redis does for scores and floats.</summary>
public static class DoubleText
{
    private const NumberStyles Styles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <summary>
    /// Parses a finite or infinite number. <c>inf</c>, <c>+inf</c> and <c>-inf</c> are accepted
    /// in any case; NaN and surrounding whitespace are rejected.
    /// </summary>
    public static bool TryParse(string text, out double value)
    {
        string trimmed = text.StartsWith('+') ? text[1..] : text;
        if (trimmed.Equals("inf", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("infinity", StringComparison.OrdinalIgnoreCase))
        {
            value = text.StartsWith('-') ? double.NegativeInfinity : double.PositiveInfinity;
            return true;
        }

        if (text.Equals("-inf", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
        {
            value = double.NegativeInfinity;
            return true;
        }

        return double.TryParse(text, Styles, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value);
    }

    /// <summary>
    /// Formats with the shortest text that round-trips: <c>1</c>, <c>1.5</c>, <c>1e+20</c>,
    /// <c>inf</c>, <c>-inf</c>.
    /// </summary>
    public static string Format(double value) => value switch
    {
        double.PositiveInfinity => "inf",
        double.NegativeInfinity => "-inf",
        _ => value.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e'),
    };
}
