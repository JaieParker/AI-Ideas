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

    [Fact(DisplayName = "BR-SKILL-010 — /skill-bootstrap is the single named exemption from the dispatch convention")]
    public void Skill_Bootstrap_Is_The_Single_Named_Exemption()
    {
        var skillDirs = Directory.GetDirectories(SkillsRoot);
        var exemptions = new List<string>();

        foreach (var dir in skillDirs)
        {
            var skillName = Path.GetFileName(dir);
            var skillMd = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillMd)) continue;

            var bangLine = ReadBangLine(skillMd);
            if (bangLine is null) continue;

            if (!bangLine.Contains(DispatchPrefix))
                exemptions.Add(skillName);
        }

        Assert.Equal(new[] { BootstrapSkillName }, exemptions.OrderBy(s => s).ToArray());
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
