namespace HelpersSidecar.Tests.SkillConventions;

/// <summary>
/// BR-SKILL-010 — every dispatching skill probes the helpers sidecar
/// at :5050 and falls back to a PRECONDITION_FAIL marker that refers
/// the user to /skill-bootstrap. /skill-bootstrap is the only named
/// exemption — its `!` line probes :5050/healthz directly because its
/// job is to bring the sidecar up.
/// </summary>
public class SkillPreconditionLintTests
{
    private static readonly string ProjectRoot = LocateProjectRoot();
    private static readonly string SkillsRoot = Path.Combine(ProjectRoot, ".claude", "skills");

    private const string DispatchPrefix = "curl http://127.0.0.1:5050/skills/";
    private const string PreconditionMarker = "|| printf 'PRECONDITION_FAIL:";
    private const string BootstrapReference = "/skill-bootstrap";
    private const string BootstrapSkillName = "skill-bootstrap";
    private const string BootstrapProbe = "curl http://127.0.0.1:5050/healthz";

    // Plan-23 / BR-SKILL-007 amended — orchestrator skills run their
    // chain inside the agent turn (Bash + Skill tools) so chained
    // skills traverse the Claude Code harness and emit
    // claude_code.skill_activated events. A `!` exec line fires
    // before the agent turn starts, so it would defeat that
    // purpose. /demo is the canonical orchestrator.
    private static readonly HashSet<string> OrchestratorSkills = new() { "demo" };

    [Fact(DisplayName = "BR-SKILL-010 — every dispatching skill has a PRECONDITION_FAIL fallback referencing /skill-bootstrap")]
    public void Every_Dispatching_Skill_Has_Precondition_Fallback()
    {
        Assert.True(Directory.Exists(SkillsRoot), $"Skills root not found at {SkillsRoot}");

        var skillDirs = Directory.GetDirectories(SkillsRoot);
        Assert.NotEmpty(skillDirs);

        var failures = new List<string>();

        foreach (var dir in skillDirs)
        {
            var skillName = Path.GetFileName(dir);
            var skillMd = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillMd)) continue;

            var bangLine = ReadBangLine(skillMd);

            if (OrchestratorSkills.Contains(skillName))
            {
                // Orchestrators MUST NOT have a `!` exec line — their
                // workflow runs in the agent turn. Inverted assertion.
                if (bangLine is not null)
                    failures.Add($"{skillName}: orchestrator skills must NOT have a `!` exec line (Plan-23 / BR-SKILL-007 amended)");
                continue;
            }

            if (bangLine is null)
            {
                failures.Add($"{skillName}: SKILL.md has no `!` exec line");
                continue;
            }

            if (skillName == BootstrapSkillName)
            {
                if (!bangLine.Contains(BootstrapProbe))
                    failures.Add($"{skillName}: bootstrap skill must probe :5050/healthz directly, not via /skills/.../dispatch");
                if (bangLine.Contains(DispatchPrefix))
                    failures.Add($"{skillName}: bootstrap skill must NOT dispatch via :5050/skills/.../dispatch");
                continue;
            }

            if (!bangLine.Contains(DispatchPrefix))
                failures.Add($"{skillName}: dispatching skills must curl :5050/skills/<name>/dispatch (got: {Truncate(bangLine, 80)})");

            if (!bangLine.Contains(PreconditionMarker))
                failures.Add($"{skillName}: missing `{PreconditionMarker}` fallback in `!` exec line");

            if (!bangLine.Contains(BootstrapReference))
                failures.Add($"{skillName}: PRECONDITION_FAIL message must reference {BootstrapReference}");
        }

        Assert.True(failures.Count == 0,
            "BR-SKILL-010 violations:\n  - " + string.Join("\n  - ", failures));
    }

    [Fact(DisplayName = "BR-SKILL-010 — /skill-bootstrap is the single ! exec exemption (orchestrators are no-! exec, separate clause)")]
    public void Skill_Bootstrap_Is_The_Single_Named_BangLine_Exemption()
    {
        var skillDirs = Directory.GetDirectories(SkillsRoot);
        var exemptions = new List<string>();

        foreach (var dir in skillDirs)
        {
            var skillName = Path.GetFileName(dir);
            if (OrchestratorSkills.Contains(skillName)) continue;

            var skillMd = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillMd)) continue;

            var bangLine = ReadBangLine(skillMd);
            if (bangLine is null) continue;

            if (!bangLine.Contains(DispatchPrefix))
                exemptions.Add(skillName);
        }

        Assert.Equal(new[] { BootstrapSkillName }, exemptions.OrderBy(s => s).ToArray());
    }

    [Fact(DisplayName = "BR-SKILL-007 (amended Plan-23) — orchestrator skills have NO ! exec line")]
    public void Orchestrator_Skills_Have_No_BangLine()
    {
        foreach (var skillName in OrchestratorSkills)
        {
            var skillMd = Path.Combine(SkillsRoot, skillName, "SKILL.md");
            Assert.True(File.Exists(skillMd), $"orchestrator SKILL.md missing: {skillMd}");
            Assert.Null(ReadBangLine(skillMd));
        }
    }

    private static string? ReadBangLine(string skillMdPath)
    {
        var lines = File.ReadAllLines(skillMdPath);
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("!`") && trimmed.EndsWith("`"))
                return trimmed[2..^1];
        }
        return null;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    private static string LocateProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".claude", "skills"))
                && File.Exists(Path.Combine(dir, "OTEL.slnx")))
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir || parent is null) break;
            dir = parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate project root from " + AppContext.BaseDirectory);
    }
}
