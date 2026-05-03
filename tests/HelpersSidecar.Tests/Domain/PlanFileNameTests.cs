using HelpersSidecar.Domain;

namespace HelpersSidecar.Tests.Domain;

/// <summary>
/// BR-EXTEND-004 / BR-EXTEND-006 — plan filename parsing and
/// rendering against a domain's <see cref="PlanFileConventions"/>.
/// All cases here use the OTEL conventions; per-domain coverage
/// for any future domain (kai-platform) lands alongside that
/// domain's tests.
/// </summary>
public class PlanFileNameTests
{
    private static readonly PlanFileConventions Otel = new("The-OTEL-Plan");

    [Fact(DisplayName = "BR-EXTEND-004 — base filename parses as N=1, no slug")]
    public void Base_File_Parses_As_N1()
    {
        var p = PlanFileName.TryParse("The-OTEL-Plan.md", Otel);
        Assert.NotNull(p);
        Assert.Equal(1, p!.Number);
        Assert.Null(p.Slug);
    }

    [Theory(DisplayName = "BR-EXTEND-004 — numbered files parse")]
    [InlineData("The-OTEL-Plan-2.md", 2, null)]
    [InlineData("The-OTEL-Plan-7.md", 7, null)]
    [InlineData("The-OTEL-Plan-42.md", 42, null)]
    public void Numbered_Files_Parse(string fileName, int n, string? slug)
    {
        var p = PlanFileName.TryParse(fileName, Otel);
        Assert.NotNull(p);
        Assert.Equal(n, p!.Number);
        Assert.Equal(slug, p.Slug);
    }

    [Theory(DisplayName = "BR-EXTEND-004 — numbered + slug files parse")]
    [InlineData("The-OTEL-Plan-2-foo.md", 2, "foo")]
    [InlineData("The-OTEL-Plan-3-fix-the-foo.md", 3, "fix-the-foo")]
    [InlineData("The-OTEL-Plan-9-a1-b2.md", 9, "a1-b2")]
    public void Numbered_With_Slug_Parses(string fileName, int n, string slug)
    {
        var p = PlanFileName.TryParse(fileName, Otel);
        Assert.NotNull(p);
        Assert.Equal(n, p!.Number);
        Assert.Equal(slug, p.Slug);
    }

    [Theory(DisplayName = "BR-EXTEND-004 — non-matching filenames return null")]
    [InlineData("The-OTEL-Plan-foo.md")]    // letters where N expected
    [InlineData("The-OTEL.md")]
    [InlineData("Plan-2.md")]
    [InlineData("The-OTEL-Plan.txt")]       // wrong extension
    [InlineData("The-OTEL-Plan-2-FOO.md")]  // uppercase in slug position
    [InlineData("the-otel-plan.md")]        // wrong case in prefix
    [InlineData("The-OTEL-Plan-.md")]       // dangling dash
    [InlineData("")]
    public void Non_Matching_Returns_Null(string fileName)
    {
        Assert.Null(PlanFileName.TryParse(fileName, Otel));
    }

    [Fact(DisplayName = "BR-EXTEND-004 — null filename returns null")]
    public void Null_Returns_Null()
    {
        Assert.Null(PlanFileName.TryParse(null, Otel));
    }

    [Theory(DisplayName = "BR-EXTEND-004 — FileName renders the canonical form")]
    [InlineData(1, null, "The-OTEL-Plan.md")]
    [InlineData(2, null, "The-OTEL-Plan-2.md")]
    [InlineData(2, "foo", "The-OTEL-Plan-2-foo.md")]
    [InlineData(7, "fix-the-foo", "The-OTEL-Plan-7-fix-the-foo.md")]
    public void FileName_Renders_Canonical_Form(int n, string? slug, string expected)
    {
        var p = new PlanFileName(n, slug, Otel);
        Assert.Equal(expected, p.FileName);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — Directory defaults to '.' for back-compat (Plan-9)")]
    public void Directory_Defaults_To_Dot()
    {
        var defaulted = new PlanFileConventions("Whatever-Plan");
        Assert.Equal(".", defaulted.Directory);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — Directory is exposed as a record property (Plan-9)")]
    public void Directory_Is_Exposed()
    {
        var conv = new PlanFileConventions("Whatever-Plan", 1, "docs/whatever/plans");
        Assert.Equal("docs/whatever/plans", conv.Directory);
    }

    [Theory(DisplayName = "BR-EXTEND-006 — Directory does not affect FileNameFor (Plan-9)")]
    [InlineData(".")]
    [InlineData("docs/otel/plans")]
    [InlineData("totally/elsewhere")]
    public void Directory_Does_Not_Affect_FileName(string dir)
    {
        var conv = new PlanFileConventions("The-OTEL-Plan", 1, dir);
        Assert.Equal("The-OTEL-Plan-7-foo.md", conv.FileNameFor(7, "foo"));
        Assert.Equal("The-OTEL-Plan.md", conv.FileNameFor(1, null));
    }

    [Theory(DisplayName = "BR-EXTEND-006 — Directory does not affect TryParse (Plan-9)")]
    [InlineData(".")]
    [InlineData("docs/otel/plans")]
    public void Directory_Does_Not_Affect_TryParse(string dir)
    {
        var conv = new PlanFileConventions("The-OTEL-Plan", 1, dir);
        Assert.True(conv.TryParse("The-OTEL-Plan-7-foo.md", out var n, out var slug));
        Assert.Equal(7, n);
        Assert.Equal("foo", slug);
    }
}
