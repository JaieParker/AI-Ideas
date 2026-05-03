namespace HelpersSidecar.Application;

/// <summary>
/// Anthropic's 4 D AI-fluency framework: Delegation, Description,
/// Discernment, Diligence. BR-SKILL-013 anchors every test in this
/// project against these dimensions.
/// </summary>
public enum AiLevelDimension
{
    Delegation,
    Description,
    Discernment,
    Diligence,
}

public enum AiLevelScore
{
    Absent  = 0,
    Partial = 1,
    Strong  = 2,
}

/// <summary>
/// One deterministic check the sidecar runs against a parsed
/// <see cref="SkillFile"/>. Score is the value this check
/// contributes if it passes; absence contributes 0. Multiple
/// checks per dimension combine via <see cref="AiLevelChecker"/>'s
/// scoring rule (any partial → at most Partial; all strong →
/// Strong).
/// </summary>
public sealed record AiLevelCheck(
    AiLevelDimension Dimension,
    string           Name,
    string           Description,
    AiLevelScore     Contribution,
    Func<SkillFile, AiLevelCheckResult> Run);

public sealed record AiLevelCheckResult(bool Passed, string Evidence);

public sealed record AiLevelDimensionScore(
    AiLevelDimension Dimension,
    AiLevelScore Score,
    IReadOnlyList<AiLevelCheckOutcome> Outcomes);

public sealed record AiLevelCheckOutcome(
    string CheckName,
    bool Passed,
    string Evidence);

public sealed record AiLevelSkillScore(
    string SkillName,
    IReadOnlyList<AiLevelDimensionScore> Dimensions,
    int    Total)
{
    public IReadOnlyList<AiLevelDimension> WeakestDimensions =>
        Dimensions
            .Where(d => d.Score != AiLevelScore.Strong)
            .OrderBy(d => (int)d.Score)
            .Select(d => d.Dimension)
            .ToList();
}
