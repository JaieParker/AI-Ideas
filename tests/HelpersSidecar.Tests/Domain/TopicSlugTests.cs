using HelpersSidecar.Domain;

namespace HelpersSidecar.Tests.Domain;

/// <summary>BR-EXTEND-005 — topic slug normalisation rules.</summary>
public class TopicSlugTests
{
    [Theory(DisplayName = "BR-EXTEND-005 — slug is lowercased")]
    [InlineData("FOO", "foo")]
    [InlineData("FixThe Foo", "fixthe-foo")]
    [InlineData("Fix THE FOO Bar", "fix-the-foo-bar")]
    public void Slug_Is_Lowercased(string raw, string expected)
    {
        var result = TopicSlug.TryCreate(raw);
        Assert.True(result.Ok);
        Assert.Equal(expected, result.Slug!.Value);
    }

    [Theory(DisplayName = "BR-EXTEND-005 — non-alphanumeric runs collapse to a single hyphen")]
    [InlineData("foo bar", "foo-bar")]
    [InlineData("foo!!bar", "foo-bar")]
    [InlineData("foo  bar  baz", "foo-bar-baz")]
    [InlineData("foo--bar", "foo-bar")]
    [InlineData("foo!!!  ???bar", "foo-bar")]
    public void NonAlphanumericRuns_Collapse(string raw, string expected)
    {
        var result = TopicSlug.TryCreate(raw);
        Assert.True(result.Ok);
        Assert.Equal(expected, result.Slug!.Value);
    }

    [Theory(DisplayName = "BR-EXTEND-005 — leading and trailing hyphens are trimmed")]
    [InlineData("-foo-", "foo")]
    [InlineData("---foo---", "foo")]
    [InlineData("  spaces  ", "spaces")]
    [InlineData("!!!foo!!!", "foo")]
    public void Edge_Hyphens_Are_Trimmed(string raw, string expected)
    {
        var result = TopicSlug.TryCreate(raw);
        Assert.True(result.Ok);
        Assert.Equal(expected, result.Slug!.Value);
    }

    [Fact(DisplayName = "BR-EXTEND-005 — slug is truncated to 64 characters")]
    public void Slug_Is_Truncated_To_64_Chars()
    {
        var raw = new string('a', 100);
        var result = TopicSlug.TryCreate(raw);
        Assert.True(result.Ok);
        Assert.Equal(64, result.Slug!.Value.Length);
        Assert.Equal(new string('a', 64), result.Slug.Value);
    }

    [Fact(DisplayName = "BR-EXTEND-005 — truncation does not leave a trailing hyphen")]
    public void Truncation_Strips_Trailing_Hyphen()
    {
        // 64 chars of "ab-" pattern → "ab-ab-ab-...-ab" then truncated mid-pattern;
        // the trim guarantees the result doesn't end with a hyphen.
        var raw = string.Join("-", Enumerable.Repeat("aa", 40));
        var result = TopicSlug.TryCreate(raw);
        Assert.True(result.Ok);
        Assert.False(result.Slug!.Value.EndsWith('-'));
        Assert.True(result.Slug.Value.Length <= 64);
    }

    [Theory(DisplayName = "BR-EXTEND-005 — input that normalises to empty is rejected")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("---")]
    [InlineData("\t\n")]
    public void Empty_After_Normalisation_Is_Rejected(string raw)
    {
        var result = TopicSlug.TryCreate(raw);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Null(result.Slug);
    }

    [Fact(DisplayName = "BR-EXTEND-005 — null input is rejected with a clear message")]
    public void Null_Input_Is_Rejected()
    {
        var result = TopicSlug.TryCreate(null);
        Assert.False(result.Ok);
        Assert.Contains("null", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "BR-EXTEND-005 — already-valid slugs round-trip unchanged")]
    [InlineData("foo")]
    [InlineData("foo-bar")]
    [InlineData("foo-bar-baz-quux")]
    [InlineData("a1-b2-c3")]
    public void Already_Valid_Slugs_RoundTrip(string raw)
    {
        var result = TopicSlug.TryCreate(raw);
        Assert.True(result.Ok);
        Assert.Equal(raw, result.Slug!.Value);
    }

    [Fact(DisplayName = "BR-EXTEND-005 — non-ASCII characters become hyphens (no transliteration)")]
    public void NonAscii_Becomes_Hyphens()
    {
        var result = TopicSlug.TryCreate("hello 世界 world");
        Assert.True(result.Ok);
        Assert.Equal("hello-world", result.Slug!.Value);
    }
}
