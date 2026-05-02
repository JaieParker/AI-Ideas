using HelpersSidecar.Domain;

namespace HelpersSidecar.Tests.Domain;

/// <summary>
/// BR-ENRICH-002 — enrichment value length cap.
/// BR-ENRICH-003 — obvious-secret-prefix warning (non-blocking).
/// </summary>
public class EnrichmentValueTests
{
    [Theory(DisplayName = "BR-ENRICH-002 — values within 4096 chars are accepted")]
    [InlineData("")]
    [InlineData("PROJ-1234")]
    [InlineData("platform")]
    [InlineData("a value with spaces and / slashes : colons")]
    public void Values_Within_Limit_Accepted(string raw)
    {
        var result = EnrichmentValue.TryCreate(raw);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(raw, result.Value!.Value);
        Assert.Empty(result.Warnings);
    }

    [Fact(DisplayName = "BR-ENRICH-002 — exactly 4096 chars is accepted")]
    public void Exactly_4096_Accepted()
    {
        var raw = new string('x', 4096);
        var result = EnrichmentValue.TryCreate(raw);
        Assert.True(result.Ok);
    }

    [Fact(DisplayName = "BR-ENRICH-002 — 4097 chars is rejected")]
    public void Over_4096_Rejected()
    {
        var raw = new string('x', 4097);
        var result = EnrichmentValue.TryCreate(raw);
        Assert.False(result.Ok);
        Assert.Contains("4096", result.Error);
    }

    [Fact(DisplayName = "BR-ENRICH-002 — null value is rejected")]
    public void Null_Rejected()
    {
        var result = EnrichmentValue.TryCreate(null);
        Assert.False(result.Ok);
        Assert.Contains("null", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "BR-ENRICH-003 — secret-shaped values warn but are not blocked")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("gho_abcdefg")]
    [InlineData("ghu_abcdefg")]
    [InlineData("ghs_abcdefg")]
    [InlineData("ghr_abcdefg")]
    [InlineData("sk-proj-abcdef")]
    [InlineData("xoxb-1234-abcd")]
    public void Secret_Shaped_Values_Warn_But_Pass(string raw)
    {
        var result = EnrichmentValue.TryCreate(raw);
        Assert.True(result.Ok);                       // BR-ENRICH-003: not blocking
        Assert.Single(result.Warnings);                // exactly one warning emitted
        Assert.Contains("secret", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "BR-ENRICH-003 — values not matching the prefix produce no warning")]
    [InlineData("PROJ-1234")]
    [InlineData("platform")]
    [InlineData("akia-but-lowercase")]   // pattern is anchored, case-sensitive
    [InlineData("xghp_oops")]            // prefix not at start
    [InlineData("")]
    public void Innocuous_Values_Have_No_Warning(string raw)
    {
        var result = EnrichmentValue.TryCreate(raw);
        Assert.True(result.Ok);
        Assert.Empty(result.Warnings);
    }
}
