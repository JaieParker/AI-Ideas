using System.Diagnostics;
using System.Text;
using HelpersSidecar.Application;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /extend-skills skill (renamed from
/// /otel-extend in Plan-5 Phase 2c). Performs deterministic
/// gathering — git state inspection, plan-file scan against the
/// resolved domain's <see cref="PlanFileConventions"/>, slug
/// normalisation — and emits a structured message that instructs
/// Claude to begin the multi-phase flow described in the skill's
/// playbook.md.
///
/// The endpoint does NOT make any changes; phase work is driven
/// from SKILL.md / playbook.md by Claude with explicit user gates.
/// </summary>
public static class ExtendSkillsDispatchEndpoint
{
    public static IEndpointRouteBuilder MapExtendSkillsDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/extend-skills/dispatch", Handle)
            .WithName("ExtendSkillsDispatch")
            .WithSummary("Skill dispatcher for /extend-skills (deterministic gathering only)")
            .WithDescription("Form-encoded session_id, args (<domain> [topic | revert | status]), and " +
                "skill_dir. Performs deterministic gathering and returns a multi-line message " +
                "for Claude to begin the multi-phase flow. NEVER mutates anything.");

        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, IPlanDirectoryScanner scanner, IDomainResolver domains)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        var args = form["args"].ToString();

        if (string.IsNullOrEmpty(sessionId))
            return Text("extend-skills failed: no session id provided");

        var verb = ExtendSkillsVerb.Parse(args);

        if (verb.Kind == ExtendSkillsVerbKind.UsageMissingDomain)
            return Text($"usage: /extend-skills <domain> [<topic> | revert | status]\n" +
                        $"known domains: {string.Join(", ", domains.KnownNames)}");

        if (!domains.TryResolve(verb.Domain, out var domain))
            return Text($"extend-skills failed: unknown domain '{verb.Domain}' " +
                        $"(known: {string.Join(", ", domains.KnownNames)})");

        return verb.Kind switch
        {
            ExtendSkillsVerbKind.Begin  => Begin(verb.Topic, scanner, domain!),
            ExtendSkillsVerbKind.Revert => Revert(domain!),
            ExtendSkillsVerbKind.Status => Status(),
            _                            => Text("usage: /extend-skills <domain> [<topic> | revert | status]"),
        };
    }

    // ---------------- handlers ----------------

    private static IResult Begin(string? topic, IPlanDirectoryScanner scanner, IDomain domain)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== /extend-skills — phase 0 gathering (domain: {domain.Name}) ===");
        sb.AppendLine();
        sb.AppendLine("BR-EXTEND-009 — once the next-plan-filename below is known, set the");
        sb.AppendLine("per-session plan enrichment so every OTEL record from this flow is");
        sb.AppendLine("tagged with the plan. The skill body emits the canonical command;");
        sb.AppendLine("the user's session runs it before Phase 1.");
        sb.AppendLine();

        var (gitOk, gitOut) = TryGitStatus();
        if (gitOk)
        {
            var clean = string.IsNullOrWhiteSpace(gitOut);
            sb.AppendLine($"git state         : {(clean ? "clean" : "DIRTY")}");
            if (!clean) sb.AppendLine($"  (uncommitted)   : {gitOut.Trim()}");
        }
        else
        {
            sb.AppendLine("git state         : not a git repository");
            sb.AppendLine("  (Phase 0 must offer to git init + baseline commit)");
        }

        // Plan-9: scan the resolved domain's plan directory rather than
        // the project root. Directory == "." preserves the pre-Plan-9
        // behaviour for any domain that hasn't opted in.
        var planDir = ResolveDomainPlanDir(domain);
        var existing = scanner.ListPlanFileNames(planDir);
        var slug = NormaliseSlug(topic);
        var next = NextPlanFileName.Compute(existing, domain.PlanFiles, slug);
        sb.AppendLine($"existing plans   : {(existing.Count == 0 ? "(none)" : string.Join(", ", existing))}");
        sb.AppendLine($"next plan file   : {next.FileName}");
        if (slug is not null && slug != topic)
            sb.AppendLine($"slug             : '{topic}' → '{slug}'");

        sb.AppendLine();
        sb.AppendLine("PLAN_TAG_ENRICHMENT (BR-EXTEND-009):");
        sb.AppendLine($"  Run BEFORE Phase 1: /enrich plan {next.FileName}");
        sb.AppendLine();
        sb.AppendLine("Now drive the flow per playbook.md:");
        sb.AppendLine($"  Phase 0  — pre-flight git checks; set plan enrichment above");
        sb.AppendLine($"  Phase 1  — draft the plan, commit with `{domain.Commits.PrefixFor(ExtendPhase.Plan)}` prefix");
        sb.AppendLine($"  Phase 1.5 — /architecture-review {next.FileName} (BR-PROCESS-009 gate)");
        sb.AppendLine($"  Phase 2  — implement, commit with `{domain.Commits.PrefixFor(ExtendPhase.Implement)}` prefix");
        sb.AppendLine($"  Phase 3  — build, commit with `{domain.Commits.PrefixFor(ExtendPhase.Build)}` prefix");
        sb.AppendLine($"  Phase 4  — test, commit with `{domain.Commits.PrefixFor(ExtendPhase.Test)}` prefix");
        sb.AppendLine();
        sb.AppendLine("Each phase is gated on explicit user confirmation.");
        return Text(sb.ToString());
    }

    private static IResult Revert(IDomain domain)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== /extend-skills revert — recent extend-flow commits (domain: {domain.Name}) ===");
        sb.AppendLine();

        var (ok, output) = TryGitLog();
        if (!ok)
        {
            sb.AppendLine("(could not read git log; not a git repo or git unavailable)");
            return Text(sb.ToString());
        }

        var planPrefix      = domain.Commits.PrefixFor(ExtendPhase.Plan).Trim().TrimEnd(':');
        var implementPrefix = domain.Commits.PrefixFor(ExtendPhase.Implement).TrimEnd(':');
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains($"{planPrefix}:") || l.Contains($"{implementPrefix}:")
                     || l.Contains("chore: rebuild") || l.Contains("test: green for"))
            .Take(20)
            .ToArray();

        if (lines.Length == 0)
        {
            sb.AppendLine($"(no extend-flow commits found in recent history for domain '{domain.Name}')");
        }
        else
        {
            foreach (var line in lines) sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("Ask the user how far back to revert. Default: `git revert <range>`");
            sb.AppendLine("(keeps history). `git reset --hard <sha>` only on explicit request.");
        }
        return Text(sb.ToString());
    }

    private static IResult Status()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== /extend-skills status ===");
        sb.AppendLine();
        sb.AppendLine("(Phase tracking is not persisted; the most recent commit's prefix");
        sb.AppendLine(" tells you where the last flow ended. Use `/extend-skills <domain> revert` to");
        sb.AppendLine(" see the last few extend-flow commits.)");

        var (ok, output) = TryGitLog(maxCount: 5);
        if (ok && !string.IsNullOrWhiteSpace(output))
        {
            sb.AppendLine();
            sb.AppendLine("Last 5 commits:");
            sb.Append(output);
        }
        return Text(sb.ToString());
    }

    // ---------------- helpers ----------------

    private static string ResolveDomainPlanDir(IDomain domain)
    {
        var dir = domain.PlanFiles.Directory;
        if (string.IsNullOrEmpty(dir) || dir == ".")
            return Directory.GetCurrentDirectory();
        return Path.IsPathRooted(dir) ? dir : Path.Combine(Directory.GetCurrentDirectory(), dir);
    }

    private static string? NormaliseSlug(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return null;
        var result = TopicSlug.TryCreate(topic);
        return result.Ok ? result.Slug!.Value : null;
    }

    private static (bool ok, string output) TryGitStatus()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "status --porcelain")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (false, "");
            p.WaitForExit(2000);
            if (p.ExitCode != 0) return (false, "");
            return (true, p.StandardOutput.ReadToEnd());
        }
        catch { return (false, ""); }
    }

    private static (bool ok, string output) TryGitLog(int maxCount = 20)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"log -n {maxCount} --oneline")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (false, "");
            p.WaitForExit(2000);
            if (p.ExitCode != 0) return (false, "");
            return (true, p.StandardOutput.ReadToEnd());
        }
        catch { return (false, ""); }
    }

    private static IResult Text(string text) => Results.Text(text, "text/plain");
}
