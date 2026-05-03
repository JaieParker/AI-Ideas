using System.Text;
using System.Text.Json;
using HelpersSidecar.Infrastructure;
using Microsoft.Extensions.Options;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the skill-rewriter — exposes
/// <see cref="ISkillRewriter"/> as a sidecar HTTP API. Verbs:
/// <c>doctor</c> (drift detection), <c>repair</c> (apply edits),
/// <c>dry-run</c> (return the diff without writing).
///
/// Output schema: <c>SKILL_REWRITE_REPORT v1</c>
/// (BR-PROCESS-013). Inline-only — never written to disk;
/// not registered in <see cref="HelpersSidecar.Artefacts.IArtefactRegistry"/>
/// per the BR-PROCESS-015 resolution recorded on Plan-13.
/// </summary>
public static class SkillRewriteDispatchEndpoint
{
    private const string SkillsRoot = ".claude/skills";

    public static IEndpointRouteBuilder MapSkillRewriteDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/skill-rewrite/dispatch", Handle)
            .WithName("SkillRewriteDispatch")
            .WithSummary("Skill dispatcher for the rewriter — doctor / repair / dry-run")
            .WithDescription("Form-encoded session_id, args. " +
                "args = '<verb> [target-mode]'. verb ∈ {doctor, repair, dry-run}. " +
                "target-mode is required when verb is set-mode and ∈ {direct, container}.");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx,
        ISkillRewriter rewriter,
        IOptions<SidecarOptions> sidecar,
        IOptions<CollectorOptions> collector)
    {
        var form = await ctx.Request.ReadFormAsync();
        var args = form["args"].ToString().Trim();
        var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return Text("usage: doctor | repair | dry-run | set-mode <direct|container>");

        var verb = tokens[0];

        // Compose the rewrite spec from current settings. Today's
        // canonical spec is the SidecarMode + the docker patterns
        // it implies; future kinds extend the switch below.
        var spec = ComposeSpec(sidecar.Value, collector.Value);

        var reports = verb switch
        {
            "doctor"   => rewriter.Doctor(SkillsRoot, spec),
            "repair"   => rewriter.Repair(SkillsRoot, spec),
            "dry-run"  => rewriter.DryRun(SkillsRoot, spec),
            "set-mode" => HandleSetMode(rewriter, sidecar.Value, collector.Value, tokens),
            _          => throw new InvalidOperationException($"unknown verb: {verb}"),
        };

        return Text(Render(verb, spec, reports));
    }

    private static IReadOnlyList<DriftReport> HandleSetMode(
        ISkillRewriter rewriter, SidecarOptions sidecar, CollectorOptions collector, string[] tokens)
    {
        if (tokens.Length < 2)
            throw new InvalidOperationException("set-mode requires a target mode argument: direct | container");

        var modeToken = tokens[1];
        var targetMode = modeToken.Equals("container", StringComparison.OrdinalIgnoreCase)
            ? SidecarMode.Container
            : modeToken.Equals("direct", StringComparison.OrdinalIgnoreCase)
                ? SidecarMode.Direct
                : throw new InvalidOperationException($"unknown mode '{modeToken}': expected 'direct' or 'container'");

        var spec = new RewriteSpec.SetSidecarMode(
            TargetMode: targetMode,
            LifecycleSkillName: "skill-bootstrap",
            ContainerImage: sidecar.Container.Image,
            HostPort: sidecar.Container.HostPort,
            ContainerNamePrefix: $"claude-helpers-sidecar-{sidecar.Container.NameSuffix}");

        return rewriter.Repair(SkillsRoot, spec);
    }

    private static RewriteSpec ComposeSpec(SidecarOptions sidecar, CollectorOptions collector) =>
        new RewriteSpec.SetSidecarMode(
            TargetMode: sidecar.Mode,
            LifecycleSkillName: "skill-bootstrap",
            ContainerImage: sidecar.Container.Image,
            HostPort: sidecar.Container.HostPort,
            ContainerNamePrefix: $"claude-helpers-sidecar-{sidecar.Container.NameSuffix}");

    private static string Render(string verb, RewriteSpec spec, IReadOnlyList<DriftReport> reports)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SKILL_REWRITE_REPORT v1");
        sb.AppendLine($"verb: {verb}");
        sb.AppendLine($"spec: {spec.GetType().Name}");
        sb.AppendLine();

        var drifted = 0;
        foreach (var r in reports)
        {
            if (!r.Drifted) continue;
            drifted++;
            sb.AppendLine($"FILE: {r.FilePath}");
            foreach (var c in r.Changes) sb.AppendLine($"  - {c}");
        }
        sb.AppendLine();
        sb.AppendLine($"RESULT: {drifted}/{reports.Count} files {(verb == "doctor" || verb == "dry-run" ? "drifted" : "rewritten")}");
        return sb.ToString();
    }

    private static IResult Text(string text) => Results.Text(text, "text/plain");
}
