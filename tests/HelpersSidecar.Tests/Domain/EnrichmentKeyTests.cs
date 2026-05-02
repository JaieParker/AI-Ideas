using HelpersSidecar.Domain;

namespace HelpersSidecar.Tests.Domain;

/// <summary>BR-ENRICH-001 — enrichment key syntax.</summary>
public class EnrichmentKeyTests
{
    [Theory(DisplayName = "BR-ENRICH-001 — valid keys are accepted")]
    [InlineData("a")]
    [InlineData("team")]
    [InlineData("ticket.id")]
    [InlineData("feature_flag")]
    [InlineData("team.123-platform")]
    [InlineData("a1.b2_c3-d4")]
    public void Valid_Keys_Accepted(string raw)
    {
        var result = EnrichmentKey.TryCreate(raw);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(raw, result.Key!.Value);
    }

    [Fact(DisplayName = "BR-ENRICH-001 — exactly 64 chars is accepted")]
    public void Exactly_64_Chars_Accepted()
    {
        var raw = "a" + new string('1', 63);
        Assert.Equal(64, raw.Length);
        var result = EnrichmentKey.TryCreate(raw);
        Assert.True(result.Ok);
    }

    [Fact(DisplayName = "BR-ENRICH-001 — 65 chars is rejected")]
    public void Over_64_Chars_Rejected()
    {
        var raw = "a" + new string('1', 64);
        Assert.Equal(65, raw.Length);
        var result = EnrichmentKey.TryCreate(raw);
        Assert.False(result.Ok);
        Assert.Contains("64", result.Error);
    }

    [Theory(DisplayName = "BR-ENRICH-001 — uppercase is rejected")]
    [InlineData("Team")]
    [InlineData("TICKET")]
    [InlineData("ticketId")]
    public void Uppercase_Rejected(string raw)
    {
        var result = EnrichmentKey.TryCreate(raw);
        Assert.False(result.Ok);
    }

    [Theory(DisplayName = "BR-ENRICH-001 — must start with a-z")]
    [InlineData("1team")]
    [InlineData("_team")]
    [InlineData(".team")]
    [InlineData("-team")]
    public void Must_Start_With_Lowercase_Letter(string raw)
    {
        var result = EnrichmentKey.TryCreate(raw);
        Assert.False(result.Ok);
    }

    [Theory(DisplayName = "BR-ENRICH-001 — disallowed characters are rejected")]
    [InlineData("team!")]
    [InlineData("team space")]
    [InlineData("team:foo")]
    [InlineData("team/foo")]
    [InlineData("team\\foo")]
    [InlineData("team$foo")]
    public void Disallowed_Characters_Rejected(string raw)
    {
        var result = EnrichmentKey.TryCreate(raw);
        Assert.False(result.Ok);
    }

    [Fact(DisplayName = "BR-ENRICH-001 — null key is rejected")]
    public void Null_Rejected()
    {
        var result = EnrichmentKey.TryCreate(null);
        Assert.False(result.Ok);
        Assert.Contains("null", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "BR-ENRICH-001 — empty key is rejected")]
    public void Empty_Rejected()
    {
        var result = EnrichmentKey.TryCreate("");
        Assert.False(result.Ok);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
