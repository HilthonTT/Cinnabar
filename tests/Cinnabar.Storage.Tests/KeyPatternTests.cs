namespace Cinnabar.Storage.Tests;

public class KeyPatternTests
{
    [Theory]
    [InlineData("*", "")]
    [InlineData("*", "anything")]
    [InlineData("user:*", "user:")]
    [InlineData("user:*", "user:42")]
    [InlineData("*:42", "user:42")]
    [InlineData("a*b*c", "aXXbYYc")]
    [InlineData("a*b*c", "abc")]
    [InlineData("**", "x")]
    [InlineData("a***b", "ab")]
    [InlineData("a**b", "aXYb")]
    public void Star_matches_any_run(string pattern, string text)
    {
        KeyPattern.IsMatch(pattern, text).Should().BeTrue();
    }

    [Theory]
    [InlineData("user:*", "users:1")]
    [InlineData("*:42", "user:43")]
    [InlineData("a*b*c", "aXXbYY")]
    [InlineData("a**b", "aXYc")]
    public void Star_does_not_match_wrong_text(string pattern, string text)
    {
        KeyPattern.IsMatch(pattern, text).Should().BeFalse();
    }

    [Theory]
    [InlineData("h?llo", "hello", true)]
    [InlineData("h?llo", "hallo", true)]
    [InlineData("h?llo", "hllo", false)]
    [InlineData("h?llo", "heello", false)]
    [InlineData("?", "", false)]
    [InlineData("??", "ab", true)]
    public void Question_mark_matches_exactly_one_character(string pattern, string text, bool expected)
    {
        KeyPattern.IsMatch(pattern, text).Should().Be(expected);
    }

    [Theory]
    [InlineData("h[ae]llo", "hello", true)]
    [InlineData("h[ae]llo", "hallo", true)]
    [InlineData("h[ae]llo", "hillo", false)]
    [InlineData("h[ae]llo", "hllo", false)]
    public void Class_matches_listed_characters(string pattern, string text, bool expected)
    {
        KeyPattern.IsMatch(pattern, text).Should().Be(expected);
    }

    [Theory]
    [InlineData("h[^e]llo", "hallo", true)]
    [InlineData("h[^e]llo", "hello", false)]
    [InlineData("h[^ab]llo", "hcllo", true)]
    [InlineData("h[^ab]llo", "hbllo", false)]
    public void Negated_class_matches_other_characters(string pattern, string text, bool expected)
    {
        KeyPattern.IsMatch(pattern, text).Should().Be(expected);
    }

    [Theory]
    [InlineData("h[a-c]llo", "hbllo", true)]
    [InlineData("h[a-c]llo", "hallo", true)]
    [InlineData("h[a-c]llo", "hcllo", true)]
    [InlineData("h[a-c]llo", "hdllo", false)]
    [InlineData("h[c-a]llo", "hbllo", true)]
    [InlineData("[0-9][0-9]", "42", true)]
    [InlineData("[0-9][0-9]", "4x", false)]
    [InlineData("[^0-9]", "x", true)]
    [InlineData("[^0-9]", "5", false)]
    public void Range_class_matches_characters_in_range(string pattern, string text, bool expected)
    {
        KeyPattern.IsMatch(pattern, text).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"a\*b", "a*b", true)]
    [InlineData(@"a\*b", "aXb", false)]
    [InlineData(@"a\?b", "a?b", true)]
    [InlineData(@"a\?b", "aXb", false)]
    [InlineData(@"\[x\]", "[x]", true)]
    [InlineData(@"a\\b", @"a\b", true)]
    [InlineData(@"[\]]", "]", true)]
    [InlineData(@"[\-]", "-", true)]
    public void Backslash_escapes_special_characters(string pattern, string text, bool expected)
    {
        KeyPattern.IsMatch(pattern, text).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("", "a", false)]
    [InlineData("a", "", false)]
    [InlineData("*", "", true)]
    [InlineData("[a]", "", false)]
    public void Empty_pattern_and_text(string pattern, string text, bool expected)
    {
        KeyPattern.IsMatch(pattern, text).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello", "hello", true)]
    [InlineData("hello", "Hello", false)]
    [InlineData("hello", "hello!", false)]
    [InlineData("hello", "hell", false)]
    public void Literal_pattern_matches_exactly(string pattern, string text, bool expected)
    {
        KeyPattern.IsMatch(pattern, text).Should().Be(expected);
    }

    [Fact]
    public void Many_stars_do_not_backtrack_exponentially()
    {
        // KEYS with a crafted pattern must not hang the server (cf. Redis CVE-2022-36021).
        // Sized so a naive backtracking matcher takes seconds; a linear/greedy one takes microseconds.
        string pattern = string.Concat(Enumerable.Repeat("a*", 11)) + "b";
        string text = new('a', 30);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool matched = KeyPattern.IsMatch(pattern, text);
        stopwatch.Stop();

        matched.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }
}
