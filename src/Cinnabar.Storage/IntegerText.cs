using System.Globalization;

namespace Cinnabar.Storage;

/// <summary>Parses stored integers as strictly as Redis's <c>string2ll</c>.</summary>
public static class IntegerText
{
    /// <summary>
    /// Parses a 64-bit integer. Rejects what Redis rejects: an explicit <c>+</c>, leading
    /// zeros, <c>-0</c>, whitespace, and values out of range.
    /// </summary>
    public static bool TryParse(string text, out long value)
    {
        value = 0;
        if (text.Length is 0 or > 20)
        {
            return false;
        }

        if (text == "0")
        {
            return true;
        }

        int first = text[0] == '-' ? 1 : 0;
        if (first >= text.Length || text[first] is < '1' or > '9')
        {
            return false;
        }

        for (int i = first + 1; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return false;
            }
        }

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
