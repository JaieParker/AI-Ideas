using System.Text;
using HelpersSidecar.Application;
using HelpersSidecar.Artefacts;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /ai-level skill (Plan-10). Scores
/// one skill or every local skill against the 4 D rubric
/// (BR-SKILL-013), composes the AI_LEVEL_REPORT v1, writes the
/// report file, and returns a structured payload that the skill
/// body shows the user.
///
/// <para>Per BR-SKILL-006 / BR-SKILL-012: this endpoint runs the
/// deterministic half of the rubric. The judgement half (does
/// the description disambiguate, does the body enable
/// verification) is left to Claude reading the rendered report
/// in-session.</para>
/// </summary>
public static class AiLevelDispatchEndpoint
{
    public static IEndpointRouteBuilder MapAiLevelDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/ai-level/dispatch", Handle)
            .WithName("AiLevelDispatch")
            .WithSummary("Skill dispatcher for /ai-level — score skills against the 4 D rubric")
            .WithDescription("Form-encoded session_id and args. args is empty (usage), " +
                "<skill-name> (single skill), or 'local' (every skill in .claude/skills/). " +
                "Global scope is intentionally not supported in v1 (BR-SECURITY-003).");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, AiLevelChecker checker, AiLevelReportWriter writer, IArtefactWriter artefacts)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        var args = form["args"].ToString().Trim();

        if (string.IsNullOrEmpty(sessionId))
            return Text("ai-level failed: no session id provided");

        var localSkillsRoot = Path.Combine(Directory.GetCurrentDirectory(), ".claude", "skills");

        if (string.IsNullOrEmpty(args))
            return Text(UsageMessage(localSkillsRoot));

        var (scopeLabel, skillFiles, error) = ResolveScope(args, localSkillsRoot);
        if (error is not null)
            return Text(error);

        var scores = skillFiles.Select(checker.Score).ToList();

        var payload = writer.Compose(scopeLabel, scores, DateTimeOffset.UtcNow);
        var reportPath = await WriteReportAsync(payload, scopeLabel, artefacts);

        return Text(RenderConsoleSummary(scopeLabel, payload, reportPath));
    }

    // ------------------------------------------------ scope resolution

    private static (string ScopeLabel, IReadOnlyList<SkillFile> Files, string? Error) ResolveScope(
        string args, string localSkillsRoot)
    {
        if (args == "local")
        {
            if (!Directory.Exists(localSkillsRoot))
                return ("local", Array.Empty<SkillFile>(),
                    $"ai-level failed: local skills root not found at {localSkillsRoot}");

            var files = Directory.EnumerateDirectories(localSkillsRoot)
                .Select(dir => Path.Combine(dir, "SKILL.md"))
                .Where(File.Exists)
                .Select(path => SkillFileParser.Parse(
                    skillName: Path.GetFileName(Path.GetDirectoryName(path)!),
                    fullText: File.ReadAllText(path)))
                .ToList();

            return ("local", files, null);
        }

        if (args == "global" || args == "all")
            return ("", Array.Empty<SkillFile>(),
                "ai-level failed: 'global' / 'all' scope is not supported in v1 (BR-SECURITY-003). " +
                "Use 'local' or a specific skill name.");

        // Single skill name.
        var skillDir = Path.Combine(localSkillsRoot, args);
        var skillPath = Path.Combine(skillDir, "SKILL.md");
        if (!File.Exists(skillPath))
            return (args, Array.Empty<SkillFile>(),
                $"ai-level failed: skill '{args}' not found at {skillPath}");

        var single = SkillFileParser.Parse(args, File.ReadAllText(skillPath));
        return (args, new[] { single }, null);
    }

    // ------------------------------------------------ report writing

    private static async Task<string> WriteReportAsync(AiLevelReportPayload payload, string scopeLabel, IArtefactWriter artefacts)
    {
        // Plan-11: routes through IArtefactWriter; the spec ("ai-level-report")
        // resolves the path template + destinations from ArtefactSpecs.
        var safeScope = string.IsNullOrEmpty(scopeLabel) ? "empty" : scopeLabel.Replace('/', '_');
        var body = Encoding.UTF8.GetBytes(payload.Markdown).AsMemory();
        var result = await artefacts.WriteAsync("ai-level-report",
            new Dictionary<string, string> { ["scope"] = safeScope }, body);
        return result.ResolvedKey;
    }

    // ------------------------------------------------ rendering

    private static string UsageMessage(string localSkillsRoot)
    {
        var localCount = Directory.Exists(localSkillsRoot)
            ? Directory.EnumerateDirectories(localSkillsRoot)
                .Count(d => File.Exists(Path.Combine(d, "SKILL.md")))
            : 0;

        return $"""
        usage: /ai-level [<skill-name> | local]

        Scopes:
          (empty)        usage + skill counts (this message)
          <skill-name>   score one skill in the current project
          local          score every skill under .claude/skills/

        Found {localCount} local skill(s) at {localSkillsRoot}.

        Global scope (~/.claude/skills/) is intentionally not supported in v1
        per BR-SECURITY-003. Use 'local' or a specific name.
        """;
    }

    private static string RenderConsoleSummary(string scopeLabel, AiLevelReportPayload payload, string reportPath)
    {
        var totalScore = payload.Scores.Sum(s => s.Total);
        var maxScore = payload.Scores.Count * 8;
        var weakest = payload.Scores
            .OrderBy(s => s.Total)
            .Take(3)
            .Select(s => $"{s.SkillName}: {s.Total}/8")
            .ToList();

        var summary = $"""
        === /ai-level — {scopeLabel} ===

        Skills assessed: {payload.Scores.Count}
        Total: {totalScore}/{maxScore}
        Weakest: {(weakest.Count == 0 ? "(none)" : string.Join(", ", weakest))}

        Report saved to: {reportPath}

        Read the report for the per-skill evidence and the cross-cutting
        findings. The deterministic half of the rubric is in the report;
        the judgement half is now your job (in this session) — read each
        skill's body, decide whether the description disambiguates, etc.,
        and amend the score in the report file if you disagree.
        """;
        return summary;
    }

    private static IResult Text(string text) => Results.Text(text, "text/plain");
}
