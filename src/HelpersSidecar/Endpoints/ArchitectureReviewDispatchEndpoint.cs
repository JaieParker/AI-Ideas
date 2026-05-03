using System.Text;
using HelpersSidecar.Application;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /architecture-review skill (Plan-6
/// Phase 2a — Shape B).
///
/// Per BR-SKILL-012 the dispatch endpoint is a context loader and
/// prompt renderer; the analyst is Claude (the user's session).
/// This handler:
///
/// <list type="number">
///   <item>Parses the verb (target + optional --domain=).</item>
///   <item>Loads context via <see cref="ArchitectureReviewContextLoader"/>.</item>
///   <item>Renders the context + the ARCHITECTURE_REVIEW v1 schema
///     into a structured prompt body.</item>
///   <item>Returns the prompt as text/plain. The skill body
///     instructs Claude to read the prompt and emit the
///     response per the schema.</item>
/// </list>
///
/// Phase 2a is the scaffolding: dispatch returns the rendered
/// prompt; no integration with /extend-skills, no decision gate
/// enforcement. Those land in later phases.
/// </summary>
public static class ArchitectureReviewDispatchEndpoint
{
    public static IEndpointRouteBuilder MapArchitectureReviewDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/architecture-review/dispatch", Handle)
            .WithName("ArchitectureReviewDispatch")
            .WithSummary("Skill dispatcher for /architecture-review (Shape B — Claude as analyst)")
            .WithDescription("Form-encoded session_id, args (<target> [--domain=<name>]). " +
                "Loads CLAUDE.md, business-rules, recent plans, target body, and the resolved " +
                "domain's TrustedReferences; renders a structured prompt with the " +
                "ARCHITECTURE_REVIEW v1 schema. Per BR-SKILL-012 — never performs deterministic " +
                "evaluation; Claude is the analyst.");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, IDomainResolver domains)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        var args = form["args"].ToString();

        if (string.IsNullOrEmpty(sessionId))
            return Text("architecture-review failed: no session id provided");

        var verb = ArchitectureReviewVerb.Parse(args);
        if (!verb.HasTarget)
            return Text("usage: /architecture-review <target> [--domain=<name>]\n" +
                        "  target: path | plan-id | 'current' (uncommitted diff) | branch\n" +
                        $"  domain: defaults to 'otel' (known: {string.Join(", ", domains.KnownNames)})");

        if (verb.Domain is not null && !domains.TryResolve(verb.Domain, out _))
            return Text($"architecture-review failed: unknown domain '{verb.Domain}' " +
                        $"(known: {string.Join(", ", domains.KnownNames)})");

        var loader = new ArchitectureReviewContextLoader(domains, Directory.GetCurrentDirectory());
        ArchitectureReviewContext context;
        try
        {
            context = loader.Build(verb.Target, verb.Domain);
        }
        catch (KeyNotFoundException ex)
        {
            return Text($"architecture-review failed: {ex.Message}");
        }

        var prompt = RenderPrompt(context);
        return Text(prompt);
    }

    private static string RenderPrompt(ArchitectureReviewContext c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== /architecture-review — Shape B prompt for Claude ===");
        sb.AppendLine($"Target:    {c.Target}");
        sb.AppendLine($"Domain:    {c.Domain.Name}");
        sb.AppendLine($"Plan tag:  {c.PlanTag ?? "(none — no plan enrichment for this session)"}");
        sb.AppendLine();
        sb.AppendLine("Read every section below as architectural context. Then evaluate the");
        sb.AppendLine("Target Body against the project's commitments and emit a response in");
        sb.AppendLine("EXACTLY the schema at the bottom — every BR in scope evaluated; every");
        sb.AppendLine("EXTENDS row paired with an ARCHITECTURE_DECISION_REQUIRED block; cited");
        sb.AppendLine("URLs come ONLY from the domain's TrustedReferences (or '(none)').");
        sb.AppendLine();

        AppendSection(sb, "CLAUDE.md (architectural commitments)", c.ClaudeMd);
        AppendSection(sb, "docs/business-rules.md (rule register)", c.BusinessRules);
        AppendSection(sb, "docs/process-incidents.md (priors — failure modes the project has named)", c.ProcessIncidents);

        sb.AppendLine("=== Recent plan files (most-recent first) ===");
        if (c.RecentPlans.Count == 0)
            sb.AppendLine("(none)");
        foreach (var (name, body) in c.RecentPlans)
            AppendSection(sb, name, body);

        sb.AppendLine($"=== Domain '{c.Domain.Name}' — knowledge slices ===");
        sb.AppendLine($"Glossary terms: {c.Domain.Glossary.Count} entries");
        foreach (var kv in c.Domain.Glossary)
            sb.AppendLine($"  - {kv.Key}: {kv.Value}");
        sb.AppendLine();
        sb.AppendLine($"Governed globs:");
        foreach (var glob in c.Domain.GovernedGlobs)
            sb.AppendLine($"  - {glob}");
        sb.AppendLine();
        sb.AppendLine($"BusinessRulesPath: {c.Domain.BusinessRulesPath}");
        sb.AppendLine();
        sb.AppendLine($"TrustedReferences (allow-list for CITED URLs — {c.Domain.TrustedReferences.Count} entries):");
        foreach (var r in c.Domain.TrustedReferences)
            sb.AppendLine($"  - {r.Title} — {r.Url} — {r.Why}");
        sb.AppendLine();

        AppendSection(sb, $"Target body — {c.Target}", c.TargetBody);

        sb.AppendLine("=== Response schema (emit your review per this exact shape) ===");
        sb.AppendLine(c.Schema);

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, string body)
    {
        sb.AppendLine($"=== {title} ===");
        if (string.IsNullOrEmpty(body))
            sb.AppendLine("(empty or unreadable)");
        else
            sb.AppendLine(body);
        sb.AppendLine();
    }

    private static IResult Text(string text) => Results.Text(text, "text/plain");
}
