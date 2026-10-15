namespace Cinnabar.Storage;

/// <summary>
/// Redis-style glob matching used by <c>KEYS</c>:
/// <c>*</c> (any run), <c>?</c> (one character), <c>[abc]</c>, <c>[^a]</c>, <c>[a-z]</c>,
/// and <c>\</c> to escape the next character.
/// </summary>
/// <remarks>
/// Matching is iterative with single-star backtracking, so it runs in O(pattern × text)
/// time. A naive recursive matcher is exponential on patterns like <c>a*a*a*…b</c>, which
/// would let one <c>KEYS</c> call hang the server (compare Redis CVE-2022-36021).
/// </remarks>
public static class KeyPattern
{
    /// <summary>Returns <see langword="true"/> when <paramref name="text"/> matches <paramref name="pattern"/>.</summary>
    public static bool IsMatch(string pattern, string text)
    {
        int p = 0;
        int t = 0;
        int starPattern = -1; // position just after the most recent '*'
        int starText = 0;     // text position that '*' is currently assumed to extend to

        while (t < text.Length)
        {
            if (p < pattern.Length)
            {
                if (pattern[p] == '*')
                {
                    // Start by letting the star match nothing; widen it on mismatch.
                    starPattern = ++p;
                    starText = t;
                    continue;
                }

                if (TryMatchOne(pattern, p, text[t], out int next))
                {
                    p = next;
                    t++;
                    continue;
                }
            }

            if (starPattern < 0)
            {
                return false;
            }

            // Mismatch after a star: let the star swallow one more character and retry.
            // Only the latest star needs revisiting; earlier ones can't help, which keeps
            // this linear in the pattern length per text position.
            p = starPattern;
            t = ++starText;
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary>
    /// Matches one text character against the pattern element at <paramref name="p"/>
    /// (a literal, <c>?</c>, an escape, or a character class), and returns the position
    /// after that element in <paramref name="next"/>.
    /// </summary>
    private static bool TryMatchOne(string pattern, int p, char c, out int next)
    {
        switch (pattern[p])
        {
            case '?':
                next = p + 1;
                return true;

            case '\\' when p + 1 < pattern.Length:
                next = p + 2;
                return pattern[p + 1] == c;

            case '[':
                return MatchClass(pattern, p, c, out next);

            default:
                next = p + 1;
                return pattern[p] == c;
        }
    }

    /// <summary>
    /// Matches <paramref name="c"/> against the character class starting at
    /// <paramref name="start"/> (the <c>[</c>). An unterminated class runs to the end of the pattern.
    /// </summary>
    private static bool MatchClass(string pattern, int start, char c, out int next)
    {
        int i = start + 1;
        bool negate = i < pattern.Length && pattern[i] == '^';
        if (negate)
        {
            i++;
        }

        bool matched = false;
        while (i < pattern.Length && pattern[i] != ']')
        {
            if (pattern[i] == '\\' && i + 1 < pattern.Length)
            {
                matched |= pattern[i + 1] == c;
                i += 2;
            }
            else if (i + 2 < pattern.Length && pattern[i + 1] == '-' && pattern[i + 2] != ']')
            {
                char lo = pattern[i];
                char hi = pattern[i + 2];
                if (lo > hi)
                {
                    (lo, hi) = (hi, lo);
                }

                matched |= c >= lo && c <= hi;
                i += 3;
            }
            else
            {
                matched |= pattern[i] == c;
                i++;
            }
        }

        next = i < pattern.Length ? i + 1 : pattern.Length;
        return matched != negate;
    }
}
