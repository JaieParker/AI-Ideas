using System.Text;
using System.Text.RegularExpressions;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// BR-SKILL-015 — skills own the integrity of their dependent
/// surfaces. The rewriter is the mechanism: it walks every project
/// SKILL.md, applies a typed <see cref="RewriteSpec"/>, and writes
/// the file back preserving every byte not specifically targeted
/// by the spec.
///
/// V1 scope (locked per the plan's enumeration):
/// (a) sidecar loopback URL in <c>allowed-tools</c> and the body's
///     <c>!</c> exec line — <see cref="RewriteSpec.SidecarBaseUrl"/>;
/// (b) docker-pattern presence/absence in <c>allowed-tools</c>
///     driven by <see cref="SidecarMode"/> —
///     <see cref="RewriteSpec.SetSidecarMode"/>;
/// (c) <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> in
///     <c>.claude/settings.local.json</c>'s <c>env</c> block —
///     <see cref="RewriteSpec.ClaudeCodeOtlpEndpoint"/>. Closes the
///     "skills install themselves correctly" gap that left
///     Claude Code's OTLP exporter pointing at the default
///     :4318 even when the project collector had been re-ported.
///
/// The rewriter writes only inside the project's
/// <c>.claude/skills/</c> directory (BR-SKILL-004 alignment); paths
/// outside the allow-list are refused with
/// <see cref="UnauthorizedAccessException"/>.
/// </summary>
public interface ISkillRewriter
{
    /// <summary>
    /// Walks every SKILL.md under <paramref name="skillsRoot"/>
    /// and returns one <see cref="DriftReport"/> per file
    /// whose current contents differ from the result of
    /// applying <paramref name="spec"/>. Read-only.
    /// </summary>
    IReadOnlyList<DriftReport> Doctor(string skillsRoot, RewriteSpec spec);

    /// <summary>
    /// Applies <paramref name="spec"/> across every SKILL.md
    /// under <paramref name="skillsRoot"/> and writes the
    /// changed files back. Returns the same report shape as
    /// <see cref="Doctor"/> describing what was changed.
    /// </summary>
    IReadOnlyList<DriftReport> Repair(string skillsRoot, RewriteSpec spec);

    /// <summary>
    /// Like <see cref="Repair"/> but does not write — returns
    /// the diff per file as if the repair had run.
    /// </summary>
    IReadOnlyList<DriftReport> DryRun(string skillsRoot, RewriteSpec spec);

    /// <summary>
    /// File-level rewrite — sets
    /// <c>env.OTEL_EXPORTER_OTLP_ENDPOINT</c> in the gitignored
    /// <c>.claude/settings.local.json</c> at
    /// <paramref name="claudeSettingsLocalPath"/>. Creates the
    /// file (and an empty <c>env</c> block) if missing. Returns
    /// a single-item drift report describing the change. The
    /// caller surfaces the "restart Claude Code to pick up the
    /// new env var" caveat to the user — env vars are read at
    /// process startup, not at file-read time.
    /// </summary>
    DriftReport ApplyClaudeCodeOtlpEndpoint(string claudeSettingsLocalPath, RewriteSpec.ClaudeCodeOtlpEndpoint spec, bool write);
}

/// <summary>
/// Discriminated set of supported rewrite operations. New kinds
/// are added by extending this hierarchy AND amending
/// BR-SKILL-015's enumerated v1 scope.
/// </summary>
public abstract record RewriteSpec
{
    /// <summary>
    /// Replace the literal sidecar base URL anywhere it appears
    /// inside <c>allowed-tools</c> entries or the body's <c>!</c>
    /// exec line. Both URLs MUST be the absolute form
    /// <c>http(s)://host:port</c> — paths and query strings are
    /// preserved by the rewriter, not part of the match.
    /// </summary>
    public sealed record SidecarBaseUrl(string OldBaseUrl, string NewBaseUrl) : RewriteSpec;

    /// <summary>
    /// Add or remove docker-management <c>allowed-tools</c>
    /// entries on the named lifecycle skill (today:
    /// <c>skill-bootstrap</c>) based on the target mode.
    /// </summary>
    public sealed record SetSidecarMode(SidecarMode TargetMode, string LifecycleSkillName, string ContainerImage, int HostPort, string ContainerNamePrefix) : RewriteSpec;

    /// <summary>
    /// Set <c>env.OTEL_EXPORTER_OTLP_ENDPOINT</c> in
    /// <c>.claude/settings.local.json</c> so Claude Code's
    /// upstream OTEL emitter targets the project collector's
    /// resolved URL. The settings file is gitignored
    /// (<c>.claude/settings.local.json</c> matches the existing
    /// <c>.gitignore</c> entry); this rewrite does NOT touch
    /// <c>.claude/settings.json</c>'s tracked content.
    /// Caveat: Claude Code reads env vars at process startup,
    /// so the new endpoint takes effect only on the next session.
    /// </summary>
    public sealed record ClaudeCodeOtlpEndpoint(string EndpointUrl) : RewriteSpec;
}

public sealed record DriftReport(
    string FilePath,
    bool Drifted,
    IReadOnlyList<string> Changes);

public sealed class SkillRewriter : ISkillRewriter
{
    private static readonly Regex AllowedToolsLineRegex =
        new(@"^allowed-tools:\s*(?<value>.*)$", RegexOptions.Multiline);

    private static readonly Regex BangExecLineRegex =
        new(@"^!`(?<command>[^`]*)`\s*$", RegexOptions.Multiline);

    public IReadOnlyList<DriftReport> Doctor(string skillsRoot, RewriteSpec spec) =>
        WalkAndApply(skillsRoot, spec, write: false);

    public IReadOnlyList<DriftReport> Repair(string skillsRoot, RewriteSpec spec) =>
        WalkAndApply(skillsRoot, spec, write: true);

    public IReadOnlyList<DriftReport> DryRun(string skillsRoot, RewriteSpec spec) =>
        WalkAndApply(skillsRoot, spec, write: false);

    private static List<DriftReport> WalkAndApply(string skillsRoot, RewriteSpec spec, bool write)
    {
        // BR-SKILL-004 — paths outside the allow-list are refused.
        var fullRoot = Path.GetFullPath(skillsRoot);
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException($"skills root not found: {fullRoot}");

        var reports = new List<DriftReport>();
        foreach (var skillFile in Directory.EnumerateFiles(fullRoot, "SKILL.md", SearchOption.AllDirectories))
        {
            // Defence in depth — every concrete file path is itself
            // re-validated to be under the allow-list. Symlinks etc.
            var fullPath = Path.GetFullPath(skillFile);
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"skill path escapes root: {fullPath}");

            var original = File.ReadAllText(fullPath);
            var (rewritten, changes) = ApplySpec(original, spec);

            var drifted = changes.Count > 0;
            reports.Add(new DriftReport(fullPath, drifted, changes));

            if (drifted && write && rewritten != original)
                File.WriteAllText(fullPath, rewritten);
        }
        return reports;
    }

    internal static (string Rewritten, List<string> Changes) ApplySpec(string content, RewriteSpec spec) =>
        spec switch
        {
            RewriteSpec.SidecarBaseUrl s => RewriteSidecarBaseUrl(content, s),
            RewriteSpec.SetSidecarMode m => RewriteSidecarMode(content, m),
            RewriteSpec.ClaudeCodeOtlpEndpoint => (content, new List<string>()), // file-level, not SKILL.md scope
            _ => (content, new List<string>())
        };

    // ---------------- (a) sidecar base URL rewrite ----------------

    private static (string, List<string>) RewriteSidecarBaseUrl(string content, RewriteSpec.SidecarBaseUrl spec)
    {
        var changes = new List<string>();
        if (spec.OldBaseUrl == spec.NewBaseUrl) return (content, changes);

        var rewritten = content.Replace(spec.OldBaseUrl, spec.NewBaseUrl, StringComparison.Ordinal);
        if (rewritten != content)
        {
            var hits = CountOccurrences(content, spec.OldBaseUrl);
            changes.Add($"sidecar-base-url: {spec.OldBaseUrl} → {spec.NewBaseUrl} ({hits} occurrence{(hits == 1 ? "" : "s")})");
        }
        return (rewritten, changes);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    // ---------------- (b) sidecar mode rewrite ----------------

    private static (string, List<string>) RewriteSidecarMode(string content, RewriteSpec.SetSidecarMode spec)
    {
        var changes = new List<string>();

        // Only the lifecycle skill's SKILL.md gets docker patterns.
        // Non-target skills are inspected (no-op) so the dry-run
        // includes them in the report.
        if (!IsTargetLifecycleSkill(content, spec.LifecycleSkillName))
            return (content, changes);

        var allowedToolsMatch = AllowedToolsLineRegex.Match(content);
        if (!allowedToolsMatch.Success)
            return (content, changes); // no allowed-tools line; nothing to do

        var currentValue = allowedToolsMatch.Groups["value"].Value.TrimEnd();
        var dockerPatterns = DockerPatterns(spec);

        var hasDocker = dockerPatterns.Any(p => currentValue.Contains(p, StringComparison.Ordinal));
        var wantsDocker = spec.TargetMode == SidecarMode.Container;

        if (hasDocker == wantsDocker) return (content, changes);

        string newValue;
        if (wantsDocker)
        {
            newValue = currentValue.TrimEnd() + " " + string.Join(' ', dockerPatterns);
            changes.Add($"sidecar-mode: direct → container (added {dockerPatterns.Length} docker patterns)");
        }
        else
        {
            var trimmed = currentValue;
            foreach (var pattern in dockerPatterns)
            {
                // Remove with surrounding whitespace; tolerant of order.
                trimmed = trimmed
                    .Replace(" " + pattern, string.Empty, StringComparison.Ordinal)
                    .Replace(pattern + " ", string.Empty, StringComparison.Ordinal)
                    .Replace(pattern, string.Empty, StringComparison.Ordinal);
            }
            newValue = Regex.Replace(trimmed, @"\s{2,}", " ").Trim();
            changes.Add($"sidecar-mode: container → direct (removed {dockerPatterns.Length} docker patterns)");
        }

        var fullOldLine = allowedToolsMatch.Value;
        var fullNewLine = "allowed-tools: " + newValue;
        var rewritten = content.Substring(0, allowedToolsMatch.Index)
                      + fullNewLine
                      + content.Substring(allowedToolsMatch.Index + fullOldLine.Length);

        return (rewritten, changes);
    }

    private static bool IsTargetLifecycleSkill(string content, string skillName)
    {
        var nameMatch = Regex.Match(content, @"^name:\s*(?<n>\S+)\s*$", RegexOptions.Multiline);
        return nameMatch.Success
            && string.Equals(nameMatch.Groups["n"].Value, skillName, StringComparison.Ordinal);
    }

    private static string[] DockerPatterns(RewriteSpec.SetSidecarMode spec) =>
        new[]
        {
            $"Bash(docker run -p 127.0.0.1:{spec.HostPort}:5050 {spec.ContainerImage} *)",
            $"Bash(docker stop {spec.ContainerNamePrefix}-* *)",
            "Bash(docker ps *)",
            "Bash(docker build -t " + spec.ContainerImage + " src/HelpersSidecar/*)",
        };

    // ---------------- (c) Claude Code OTLP endpoint ----------------

    public DriftReport ApplyClaudeCodeOtlpEndpoint(string claudeSettingsLocalPath,
        RewriteSpec.ClaudeCodeOtlpEndpoint spec, bool write)
    {
        const string envKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
        var fullPath = Path.GetFullPath(claudeSettingsLocalPath);
        var changes = new List<string>();

        // Read or seed the file. The settings file is JSON; we use
        // System.Text.Json's writable JsonNode tree so we preserve
        // every other key exactly as the user has it.
        System.Text.Json.Nodes.JsonObject root;
        if (File.Exists(fullPath))
        {
            try
            {
                var parsed = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fullPath));
                root = parsed as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed file — refuse to clobber. Caller surfaces.
                return new DriftReport(fullPath, false, new List<string>
                {
                    $"refused: {Path.GetFileName(fullPath)} is not valid JSON; user must fix manually before the rewriter touches it",
                });
            }
        }
        else
        {
            root = new System.Text.Json.Nodes.JsonObject();
        }

        var env = root["env"] as System.Text.Json.Nodes.JsonObject;
        if (env is null)
        {
            env = new System.Text.Json.Nodes.JsonObject();
            root["env"] = env;
        }

        var existing = env[envKey]?.GetValue<string>();
        if (string.Equals(existing, spec.EndpointUrl, StringComparison.Ordinal))
            return new DriftReport(fullPath, false, changes);

        env[envKey] = spec.EndpointUrl;
        changes.Add($"claude-code-otlp-endpoint: {existing ?? "(unset)"} → {spec.EndpointUrl} (effective on next Claude Code restart)");

        if (write)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!); } catch (IOException) { }
            File.WriteAllText(fullPath,
                root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        return new DriftReport(fullPath, true, changes);
    }
}
