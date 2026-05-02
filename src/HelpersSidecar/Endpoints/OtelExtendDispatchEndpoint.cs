using System.Diagnostics;
using System.Text;
using HelpersSidecar.Application;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /otel-extend skill. Performs the
/// deterministic gathering work — git state inspection, plan-file
/// scan, slug normalisation — and emits a structured message that
/// instructs Claude to begin the multi-phase flow described in the
/// skill's playbook.md.
///
/// The endpoint does NOT make any changes; phase work is driven
/// from SKILL.md / playbook.md by Claude with explicit user gates.
/// </summary>
public static class OtelExtendDispatchEndpoint
{
    public static IEndpointRouteBuilder MapOtelExtendDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/otel-extend/dispatch", Handle)
            .WithName("OtelExtendDispatch")
            .WithSummary("Skill dispatcher for /otel-extend (deterministic gathering only)")
            .WithDescription("Form-encoded session_id, args (topic / revert / status), and " +
                "skill_dir. Performs deterministic gathering and returns a multi-line message " +
                "for Claude to begin the multi-phase flow. NEVER mutates anything.");

        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, IPlanDirectoryScanner scanner)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        var args = form["args"].ToString();
        var skillDir = form["skill_dir"].ToString().Trim();

        if (string.IsNullOrEmpty(sessionId))
            return Text("otel-extend failed: no session id provided");

        var verb = OtelExtendVerb.Parse(args);
        return verb.Kind switch
        {
            OtelExtendVerbKind.Begin  => Begin(verb.Topic, scanner),
            OtelExtendVerbKind.Revert => Revert(),
            OtelExtendVerbKind.Status => Status(),
            _                         => Text("usage: /otel-extend [<topic> | revert | status]"),
        };
    }

    // ---------------- handlers ----------------

    private static IResult Begin(string? topic, IPlanDirectoryScanner scanner)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== /otel-extend — phase 0 gathering ===");
        sb.AppendLine();

        // Git state — best-effort. Claude will independently verify in Phase 0.
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

        // Plan-file scan via the existing /helpers/plans/next-name logic.
        var existing = scanner.ListPlanFileNames(Directory.GetCurrentDirectory());
        var slug = NormaliseSlug(topic);
        var next = NextPlanFileName.Compute(existing, slug);
        sb.AppendLine($"existing plans   : {(existing.Count == 0 ? "(none)" : string.Join(", ", existing))}");
        sb.AppendLine($"next plan file   : {next.FileName}");
        if (slug is not null && slug != topic)
            sb.AppendLine($"slug             : '{topic}' → '{slug}'");

        sb.AppendLine();
        sb.AppendLine("Now drive the flow per playbook.md:");
        sb.AppendLine("  Phase 0  — pre-flight git checks");
        sb.AppendLine("  Phase 1  — draft the plan, commit with `plan:` prefix");
        sb.AppendLine("  Phase 2  — implement, commit with `feat(otel):` prefix");
        sb.AppendLine("  Phase 3  — build, commit with `chore:` prefix");
        sb.AppendLine("  Phase 4  — test, commit with `test:` prefix");
        sb.AppendLine();
        sb.AppendLine("Each phase is gated on explicit user confirmation.");
        return Text(sb.ToString());
    }

    private static IResult Revert()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== /otel-extend revert — recent extend-flow commits ===");
        sb.AppendLine();

        var (ok, output) = TryGitLog();
        if (!ok)
        {
            sb.AppendLine("(could not read git log; not a git repo or git unavailable)");
            return Text(sb.ToString());
        }

        // Filter for extend-flow prefixes.
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("plan:") || l.Contains("feat(otel):")
                     || l.Contains("chore: rebuild") || l.Contains("test: green for"))
            .Take(20)
            .ToArray();

        if (lines.Length == 0)
        {
            sb.AppendLine("(no extend-flow commits found in recent history)");
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
        sb.AppendLine("=== /otel-extend status ===");
        sb.AppendLine();
        sb.AppendLine("(Phase tracking is not persisted; the most recent commit's prefix");
        sb.AppendLine(" tells you where the last flow ended. Use `/otel-extend revert` to");
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
