namespace HelpersSidecar.Tests.SkillConventions;

/// <summary>
/// BR-DEMO-005 — /demo and /extend-skills self-recover when the
/// deterministic-helpers sidecar (:5050) is down. The contract:
/// the SKILL.md `!` exec line falls through to the lifecycle CLI
/// probe on dispatch failure; the SKILL body documents the
/// RECOVERY_AVAILABLE v1 marker emit and the foreign-process
/// suppression rule (BR-SECURITY-003); the allowed-tools list
/// carries Skill(skill-bootstrap start *) so the chain can fire.
/// </summary>
public class DemoPreflightRecoveryTests
{
    private static readonly string ProjectRoot = LocateProjectRoot();
    private static readonly string SkillsRoot = Path.Combine(ProjectRoot, ".claude", "skills");

    private const string LifecycleProbeFallback =
        "dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll --lifecycle probe sidecar";
    private const string RecoveryChainEntry = "Skill(skill-bootstrap start *)";
    private const string RecoveryMarkerLiteral = "RECOVERY_AVAILABLE v1";
    private const string ForeignSuppressionMarker = "BR-SECURITY-003";

    [Fact(DisplayName = "BR-DEMO-005 — /extend-skills emits RECOVERY_AVAILABLE marker on sidecar-down via ! exec fallback")]
    public void ExtendSkills_Carries_The_BangLine_Recovery_Contract()
    {
        var skillMd = Path.Combine(SkillsRoot, "extend-skills", "SKILL.md");
        Assert.True(File.Exists(skillMd), $"SKILL.md missing: {skillMd}");

        var content = File.ReadAllText(skillMd);
        var bangLine = ReadBangLine(skillMd)
            ?? throw new Xunit.Sdk.XunitException("extend-skills: SKILL.md has no `!` exec line");

        Assert.True(bangLine.Contains(LifecycleProbeFallback),
            $"extend-skills: `!` line must fall through to lifecycle probe (expected substring '{LifecycleProbeFallback}')");

        var allowedTools = ReadAllowedToolsLine(skillMd)
            ?? throw new Xunit.Sdk.XunitException("extend-skills: SKILL.md has no allowed-tools line");

        Assert.True(allowedTools.Contains(RecoveryChainEntry),
            $"extend-skills: allowed-tools must include '{RecoveryChainEntry}' for the recovery chain");

        Assert.True(content.Contains(RecoveryMarkerLiteral),
            $"extend-skills: body must document the '{RecoveryMarkerLiteral}' marker emit");

        Assert.True(content.Contains(ForeignSuppressionMarker),
            $"extend-skills: body must reference '{ForeignSuppressionMarker}' for foreign-process suppression");
    }

    [Fact(DisplayName = "BR-DEMO-005 / BR-SKILL-007 (amended Plan-23) — /demo has NO ! exec line; recovery contract lives in body + allowed-tools")]
    public void Demo_Has_No_BangLine_But_Carries_Body_Recovery_Contract()
    {
        var skillMd = Path.Combine(SkillsRoot, "demo", "SKILL.md");
        Assert.True(File.Exists(skillMd), $"SKILL.md missing: {skillMd}");

        var content = File.ReadAllText(skillMd);

        // Plan-23 — /demo is an orchestrator. Its workflow runs in the
        // agent turn (Bash + Skill tools) so claude_code.skill_activated
        // events fire for chained steps; a `!` exec line would render
        // before the agent turn, defeating that purpose.
        Assert.Null(ReadBangLine(skillMd));

        var allowedTools = ReadAllowedToolsLine(skillMd)
            ?? throw new Xunit.Sdk.XunitException("demo: SKILL.md has no allowed-tools line");

        Assert.True(allowedTools.Contains(RecoveryChainEntry),
            $"demo: allowed-tools must include '{RecoveryChainEntry}' for the recovery chain");

        Assert.True(content.Contains(RecoveryMarkerLiteral),
            $"demo: body must document the '{RecoveryMarkerLiteral}' marker emit");

        Assert.True(content.Contains(ForeignSuppressionMarker),
            $"demo: body must reference '{ForeignSuppressionMarker}' for foreign-process suppression");
    }

    [Fact(DisplayName = "BR-DEMO-005 — business-rules.md documents the rule")]
    public void BusinessRules_Md_Documents_BR_DEMO_005()
    {
        var path = Path.Combine(ProjectRoot, "docs", "business-rules.md");
        Assert.True(File.Exists(path), $"business-rules.md missing: {path}");

        var content = File.ReadAllText(path);
        Assert.True(content.Contains("BR-DEMO-005"),
            "business-rules.md must contain a BR-DEMO-005 section header");
        Assert.True(content.Contains("RECOVERY_AVAILABLE v1"),
            "BR-DEMO-005 must reference the RECOVERY_AVAILABLE v1 contract");
    }

    [Fact(DisplayName = "BR-DEMO-005 — /enrich is chainable from skills (disable-model-invocation: false)")]
    public void Enrich_Is_Chainable_From_Skills()
    {
        var enrichSkillMd = Path.Combine(SkillsRoot, "enrich", "SKILL.md");
        Assert.True(File.Exists(enrichSkillMd), $"enrich SKILL.md missing: {enrichSkillMd}");

        var content = File.ReadAllText(enrichSkillMd);

        Assert.True(content.Contains("disable-model-invocation: false"),
            "/enrich frontmatter must set disable-model-invocation: false so /extend-skills can chain it for BR-EXTEND-009 plan-tag enrichment");

        Assert.False(content.Contains("User-only — only the human types /enrich"),
            "/enrich description must not call itself user-only — chainability is the design intent");
    }

    [Fact(DisplayName = "BR-DEMO-006 — /demo is model-invocable (disable-model-invocation: false)")]
    public void Demo_Is_Model_Invocable_For_Integration_Testing()
    {
        var demoSkillMd = Path.Combine(SkillsRoot, "demo", "SKILL.md");
        Assert.True(File.Exists(demoSkillMd), $"demo SKILL.md missing: {demoSkillMd}");

        var content = File.ReadAllText(demoSkillMd);

        Assert.True(content.Contains("disable-model-invocation: false"),
            "/demo frontmatter must set disable-model-invocation: false so Claude can run it for the integration-test purpose named in BR-DEMO-001 / BR-DEMO-006");
    }

    [Fact(DisplayName = "BR-PROCESS-001 — exception #3 (Plan-14 c2aca79) is named in business-rules.md")]
    public void BootstrapException_Three_Is_Named()
    {
        var path = Path.Combine(ProjectRoot, "docs", "business-rules.md");
        var content = File.ReadAllText(path);

        Assert.True(content.Contains("c2aca79"),
            "BR-PROCESS-001 exception list must name commit c2aca79 (Plan-14)");
        Assert.True(content.Contains("3."),
            "BR-PROCESS-001 must list exception #3");
    }

    private static string? ReadBangLine(string skillMdPath)
    {
        foreach (var line in File.ReadAllLines(skillMdPath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("!`") && trimmed.EndsWith("`"))
                return trimmed[2..^1];
        }
        return null;
    }

    private static string? ReadAllowedToolsLine(string skillMdPath)
    {
        foreach (var line in File.ReadAllLines(skillMdPath))
        {
            const string prefix = "allowed-tools:";
            if (line.StartsWith(prefix))
                return line[prefix.Length..].Trim();
        }
        return null;
    }

    private static string LocateProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, ".claude", "skills")) &&
                File.Exists(Path.Combine(dir, "src", "HelpersSidecar", "appsettings.json")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException("could not find repo root from " + AppContext.BaseDirectory);
    }
}
