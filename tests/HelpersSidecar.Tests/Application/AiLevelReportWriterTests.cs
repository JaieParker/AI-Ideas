using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-SKILL-013 / BR-PROCESS-013 — AiLevelReportWriter renders
/// the AI_LEVEL_REPORT v1 schema.
/// </summary>
public class AiLevelReportWriterTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "BR-PROCESS-013 — report carries the AI_LEVEL_REPORT v1 schema marker")]
    public void Report_Has_Schema_Marker()
    {
        var w = new AiLevelReportWriter();
        var payload = w.Compose("local", new[] { OneScore("foo", 8) }, FixedTime);
        Assert.Contains("AI_LEVEL_REPORT v1 — generated 2026-05-03T12:00:00Z", payload.Markdown);
        Assert.Contains("Scope: local", payload.Markdown);
    }

    [Fact(DisplayName = "BR-SKILL-013 — per-skill table renders one row per skill")]
    public void Per_Skill_Table_Has_Row_Per_Skill()
    {
        var w = new AiLevelReportWriter();
        var payload = w.Compose("local",
            new[] { OneScore("alpha", 8), OneScore("zeta", 4) }, FixedTime);
        Assert.Contains("| alpha |", payload.Markdown);
        Assert.Contains("| zeta |", payload.Markdown);
    }

    [Fact(DisplayName = "BR-SKILL-013 — empty skills list renders an aggregate placeholder")]
    public void Empty_Skills_List_Has_Placeholder()
    {
        var w = new AiLevelReportWriter();
        var payload = w.Compose("local", Array.Empty<AiLevelSkillScore>(), FixedTime);
        Assert.Contains("(no skills assessed)", payload.Markdown);
    }

    [Fact(DisplayName = "BR-SKILL-013 — per-skill evidence section names every dimension")]
    public void Evidence_Section_Has_Each_Dimension()
    {
        var w = new AiLevelReportWriter();
        var payload = w.Compose("foo", new[] { OneScore("foo", 8) }, FixedTime);
        Assert.Contains("**Delegation:", payload.Markdown);
        Assert.Contains("**Description:", payload.Markdown);
        Assert.Contains("**Discernment:", payload.Markdown);
        Assert.Contains("**Diligence:", payload.Markdown);
    }

    [Fact(DisplayName = "BR-SKILL-013 — top cross-cutting findings rolls up failures across skills")]
    public void Top_CrossCutting_Findings_Rolls_Up_Failures()
    {
        // Two skills both fail the same check → one cross-cutting finding.
        var failingOutcome = new AiLevelCheckOutcome("missing-check", false, "evidence");
        var passingOutcome = new AiLevelCheckOutcome("ok-check", true, "ok");

        var foo = new AiLevelSkillScore("foo",
            new[] { new AiLevelDimensionScore(AiLevelDimension.Discernment, AiLevelScore.Partial,
                new[] { failingOutcome, passingOutcome }) },
            Total: 1);
        var bar = new AiLevelSkillScore("bar",
            new[] { new AiLevelDimensionScore(AiLevelDimension.Discernment, AiLevelScore.Partial,
                new[] { failingOutcome, passingOutcome }) },
            Total: 1);

        var payload = new AiLevelReportWriter().Compose("local", new[] { foo, bar }, FixedTime);

        Assert.Contains("**missing-check**", payload.Markdown);
        // Skills sorted ordinal — bar before foo. Order-agnostic check on both.
        Assert.Contains("bar", payload.Markdown);
        Assert.Contains("foo", payload.Markdown);
    }

    private static AiLevelSkillScore OneScore(string name, int total)
    {
        var dims = Enum.GetValues<AiLevelDimension>()
            .Select(d => new AiLevelDimensionScore(d, AiLevelScore.Strong, Array.Empty<AiLevelCheckOutcome>()))
            .ToList();
        return new AiLevelSkillScore(name, dims, total);
    }
}
