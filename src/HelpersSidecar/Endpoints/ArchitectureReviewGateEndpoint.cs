using HelpersSidecar.Application;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Deterministic helper endpoint for BR-PROCESS-009's gate check.
/// Called by /extend-skills's Phase 1.5 → Phase 2 transition: the
/// flow refuses to proceed if the gate returns
/// <see cref="GateResult.Pass"/> = false.
///
/// Per BR-SKILL-006 this is deterministic-helper territory; the
/// LLM (Claude) NEVER decides whether a plan is "ready for Phase
/// 2" — the determinism comes from a strict regex match against
/// the plan-file body.
/// </summary>
public static class ArchitectureReviewGateEndpoint
{
    public static IEndpointRouteBuilder MapArchitectureReviewGate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/helpers/plans/architecture-review-gate",
            (ArchitectureReviewGateRequest req) =>
            {
                if (string.IsNullOrEmpty(req.PlanFile))
                    return Results.BadRequest(new { error = "PlanFile is required" });

                if (!File.Exists(req.PlanFile))
                    return Results.BadRequest(new { error = $"plan file not found: {req.PlanFile}" });

                string body;
                try { body = File.ReadAllText(req.PlanFile); }
                catch (IOException ex) { return Results.BadRequest(new { error = $"could not read {req.PlanFile}: {ex.Message}" }); }

                var result = ArchitectureReviewGateChecker.Check(body);
                return Results.Ok(new ArchitectureReviewGateResponse(
                    Pass: result.Pass,
                    Reason: result.Reason,
                    UnresolvedCommitments: result.UnresolvedCommitments));
            })
            .WithName("ArchitectureReviewGate")
            .WithSummary("Check if a plan file's EXTENDS markers all have BR-PROCESS-009 resolutions")
            .WithDescription("BR-PROCESS-009 gate. POST { PlanFile: <path> }. Returns " +
                "{ Pass, Reason, UnresolvedCommitments[] }. Pass=true when every " +
                "ARCHITECTURE_DECISION_REQUIRED block has a matching resolution in the " +
                "'## Architecture review decisions' section, or when there are no " +
                "ARCHITECTURE_DECISION_REQUIRED blocks at all.");
        return app;
    }
}

public sealed record ArchitectureReviewGateRequest(string PlanFile);
public sealed record ArchitectureReviewGateResponse(
    bool Pass,
    string Reason,
    IReadOnlyList<string> UnresolvedCommitments);
