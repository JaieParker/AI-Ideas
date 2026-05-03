using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-SKILL-013 — AiLevelChecker runs the deterministic half of
/// the 4 D rubric. Each test crafts a SkillFile fixture targeting
/// one dimension and asserts the dimension's score is the
/// expected Absent/Partial/Strong.
/// </summary>
public class AiLevelCheckerTests
{
    private readonly AiLevelChecker _checker = new();

    // ---------------- Delegation ----------------

    [Fact(DisplayName = "BR-SKILL-013 — Delegation: Strong when all three sub-checks pass")]
    public void Delegation_Strong_When_All_Pass()
    {
        var sf = MakeSkill(new()
        {
            ["disable-model-invocation"] = "true",
            ["allowed-tools"] = "Bash(curl http://127.0.0.1:5050/skills/x/dispatch *)",
        }, body: "the sidecar handles deterministic checks; Claude analyses the rest.");

        var d = ScoreFor(sf, AiLevelDimension.Delegation);
        Assert.Equal(AiLevelScore.Strong, d.Score);
    }

    [Fact(DisplayName = "BR-SKILL-013 — Delegation: Partial when allowed-tools is loose (BR-SKILL-009)")]
    public void Delegation_Partial_When_AllowedTools_Loose()
    {
        var sf = MakeSkill(new()
        {
            ["disable-model-invocation"] = "true",
            ["allowed-tools"] = "Bash(curl *)",
        }, body: "deterministic vs judgement");
        var d = ScoreFor(sf, AiLevelDimension.Delegation);
        Assert.Equal(AiLevelScore.Partial, d.Score);
        Assert.Contains(d.Outcomes, o => !o.Passed && o.CheckName == "allowed-tools-tight-prefix");
    }

    [Fact(DisplayName = "BR-SKILL-013 — Delegation: Absent when no checks pass")]
    public void Delegation_Absent_When_All_Fail()
    {
        var sf = MakeSkill(new(), body: "no relevant content");
        var d = ScoreFor(sf, AiLevelDimension.Delegation);
        Assert.Equal(AiLevelScore.Absent, d.Score);
    }

    // ---------------- Description ----------------

    [Fact(DisplayName = "BR-SKILL-013 — Description: Strong when description ≥ 50 chars + argument-hint + allowed-tools")]
    public void Description_Strong_When_All_Pass()
    {
        var sf = MakeSkill(new()
        {
            ["description"] = new string('x', 60),
            ["argument-hint"] = "<foo>",
            ["allowed-tools"] = "Bash(curl http://127.0.0.1:5050 *)",
        }, body: "");
        Assert.Equal(AiLevelScore.Strong, ScoreFor(sf, AiLevelDimension.Description).Score);
    }

    [Fact(DisplayName = "BR-SKILL-013 — Description: Partial when description is too short")]
    public void Description_Partial_When_Too_Short()
    {
        var sf = MakeSkill(new()
        {
            ["description"] = "short",
            ["argument-hint"] = "<foo>",
            ["allowed-tools"] = "Bash(curl http://127.0.0.1:5050 *)",
        }, body: "");
        var d = ScoreFor(sf, AiLevelDimension.Description);
        Assert.Equal(AiLevelScore.Partial, d.Score);
        Assert.Contains(d.Outcomes, o => !o.Passed && o.CheckName == "description-length");
    }

    // ---------------- Discernment ----------------

    [Fact(DisplayName = "BR-SKILL-013 — Discernment: Strong when body cites BR + emits schema marker")]
    public void Discernment_Strong_With_BR_And_Schema()
    {
        var sf = MakeSkill(new(), body: "Per BR-EXTEND-006 we emit DEMO_REPORT v1 …");
        Assert.Equal(AiLevelScore.Strong, ScoreFor(sf, AiLevelDimension.Discernment).Score);
    }

    [Fact(DisplayName = "BR-SKILL-013 — Discernment: Partial when only one of the two checks passes")]
    public void Discernment_Partial_When_One_Passes()
    {
        var sf = MakeSkill(new(), body: "Per BR-EXTEND-006 we do things.");  // BR yes, schema no
        Assert.Equal(AiLevelScore.Partial, ScoreFor(sf, AiLevelDimension.Discernment).Score);
    }

    [Fact(DisplayName = "BR-SKILL-013 — Discernment: Absent when neither")]
    public void Discernment_Absent_With_Neither()
    {
        var sf = MakeSkill(new(), body: "no citations or schema markers in here.");
        Assert.Equal(AiLevelScore.Absent, ScoreFor(sf, AiLevelDimension.Discernment).Score);
    }

    // ---------------- Diligence ----------------

    [Fact(DisplayName = "BR-SKILL-013 — Diligence: Strong when body has durability hint + reversal hint")]
    public void Diligence_Strong()
    {
        var sf = MakeSkill(new(), body:
            "we curl http://127.0.0.1 to dispatch; the report saves to output/. " +
            "Use revert to undo.");
        Assert.Equal(AiLevelScore.Strong, ScoreFor(sf, AiLevelDimension.Diligence).Score);
    }

    [Fact(DisplayName = "BR-SKILL-013 — Diligence: Partial with only durability OR reversal")]
    public void Diligence_Partial_With_One()
    {
        var sf = MakeSkill(new(), body: "we revert on failure (no durable artefact mentioned)");
        Assert.Equal(AiLevelScore.Partial, ScoreFor(sf, AiLevelDimension.Diligence).Score);
    }

    // ---------------- Cross-dimension / total ----------------

    [Fact(DisplayName = "BR-SKILL-013 — empty SkillFile scores 0/8")]
    public void Empty_File_Scores_Zero()
    {
        var sf = SkillFileParser.Parse("empty", string.Empty);
        var score = _checker.Score(sf);
        Assert.Equal(0, score.Total);
        Assert.All(score.Dimensions, d => Assert.Equal(AiLevelScore.Absent, d.Score));
    }

    [Fact(DisplayName = "BR-SKILL-013 — fully-conformant skill scores 8/8")]
    public void Fully_Conformant_Skill_Scores_Eight()
    {
        var sf = MakeSkill(new()
        {
            ["disable-model-invocation"] = "true",
            ["description"] = new string('x', 60),
            ["argument-hint"] = "<arg>",
            ["allowed-tools"] = "Bash(curl http://127.0.0.1:5050 *)",
        }, body: "Per BR-EXTEND-006, deterministic checks live in the sidecar. " +
                 "The body emits AI_LEVEL_REPORT v1; we curl http://127.0.0.1 and revert as needed.");

        var score = _checker.Score(sf);
        Assert.Equal(8, score.Total);
        Assert.Empty(score.WeakestDimensions);
    }

    [Fact(DisplayName = "BR-SKILL-013 — WeakestDimensions ordered by ascending score")]
    public void Weakest_Dimensions_Sorted()
    {
        var sf = MakeSkill(new()
        {
            ["description"] = new string('x', 60),
            ["argument-hint"] = "<arg>",
            ["allowed-tools"] = "Bash(curl http://127.0.0.1:5050 *)",
        }, body: "");  // Discernment + Diligence absent; Delegation partial

        var score = _checker.Score(sf);
        Assert.NotEmpty(score.WeakestDimensions);
        Assert.Contains(AiLevelDimension.Discernment, score.WeakestDimensions);
        Assert.Contains(AiLevelDimension.Diligence, score.WeakestDimensions);
    }

    private AiLevelDimensionScore ScoreFor(SkillFile sf, AiLevelDimension d) =>
        _checker.Score(sf).Dimensions.First(x => x.Dimension == d);

    private static SkillFile MakeSkill(Dictionary<string, string> frontmatter, string body) =>
        new("test", frontmatter, body);
}
