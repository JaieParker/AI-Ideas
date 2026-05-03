using HelpersSidecar.Application;
using HelpersSidecar.Domain;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-EXTEND-004 / BR-EXTEND-006 — next-plan-filename computation
/// parameterised by a domain's <see cref="PlanFileConventions"/>.
/// All cases here use the OTEL conventions; per-domain coverage
/// for any future domain lands alongside its tests.
/// </summary>
public class NextPlanFileNameTests
{
    private static readonly PlanFileConventions Otel = new("The-OTEL-Plan");

    [Fact(DisplayName = "BR-EXTEND-004 — empty list returns the base file")]
    public void Empty_List_Returns_Base()
    {
        var next = NextPlanFileName.Compute(Array.Empty<string>(), Otel);
        Assert.Equal(1, next.Number);
        Assert.Null(next.Slug);
        Assert.Equal("The-OTEL-Plan.md", next.FileName);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — only base exists → next is 2")]
    public void Only_Base_Returns_2()
    {
        var next = NextPlanFileName.Compute(new[] { "The-OTEL-Plan.md" }, Otel);
        Assert.Equal(2, next.Number);
        Assert.Equal("The-OTEL-Plan-2.md", next.FileName);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — base + 2 → next is 3")]
    public void Base_Plus_2_Returns_3()
    {
        var next = NextPlanFileName.Compute(new[] {
            "The-OTEL-Plan.md", "The-OTEL-Plan-2.md" }, Otel);
        Assert.Equal(3, next.Number);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — gaps are not filled (max + 1, not min-missing)")]
    public void Gaps_Are_Not_Filled()
    {
        var next = NextPlanFileName.Compute(new[] {
            "The-OTEL-Plan.md", "The-OTEL-Plan-3.md", "The-OTEL-Plan-5.md" }, Otel);
        Assert.Equal(6, next.Number);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — slugged files contribute their N")]
    public void Slugged_Files_Contribute_N()
    {
        var next = NextPlanFileName.Compute(new[] {
            "The-OTEL-Plan-2-foo.md", "The-OTEL-Plan-5-bar.md" }, Otel);
        Assert.Equal(6, next.Number);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — non-matching files in the list are ignored")]
    public void Unrelated_Files_Are_Ignored()
    {
        var next = NextPlanFileName.Compute(new[] {
            "The-OTEL-Plan.md",
            "The-OTEL-Plan-2.md",
            "README.md",
            "The-OTEL-Plan-foo.md",   // doesn't match (no N)
            "Plan-99.md"
        }, Otel);
        Assert.Equal(3, next.Number);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — slug is attached to the next-N filename")]
    public void Slug_Attached_To_Next()
    {
        var next = NextPlanFileName.Compute(
            new[] { "The-OTEL-Plan.md" }, Otel, slug: "fix-foo");
        Assert.Equal("The-OTEL-Plan-2-fix-foo.md", next.FileName);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — slug is ignored when no plans exist (returns base)")]
    public void Slug_Ignored_When_No_Plans()
    {
        var next = NextPlanFileName.Compute(Array.Empty<string>(), Otel, slug: "fix-foo");
        Assert.Equal("The-OTEL-Plan.md", next.FileName);
    }
}
