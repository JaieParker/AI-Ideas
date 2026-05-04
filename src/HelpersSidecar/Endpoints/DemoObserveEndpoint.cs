using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// /skills/demo/observe — receives per-step results from the live
/// /demo run (the agent POSTs one of these per executed step).
/// On <c>finalize=true</c> the accumulated state is flushed as a
/// DEMO_REPORT v1 markdown file via <see cref="IDemoReportWriter"/>
/// (BR-DEMO-004). Plan-23.
/// </summary>
public static class DemoObserveEndpoint
{
    public static IEndpointRouteBuilder MapDemoObserve(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/demo/observe", Handle)
            .WithName("DemoObserve")
            .WithSummary("Records per-step results for a /demo run; finalises DEMO_REPORT v1");
        return app;
    }

    private static async Task<IResult> Handle(
        HttpContext ctx,
        IDemoRunStore runStore,
        IDemoReportWriter reportWriter,
        ICollectorControlClient collector)
    {
        var form = await ctx.Request.ReadFormAsync();
        var runId = form["run_id"].ToString().Trim();
        if (string.IsNullOrEmpty(runId))
            return Results.Text("observe failed: missing run_id", "text/plain", statusCode: 400);

        var run = runStore.Get(runId);
        if (run is null)
            return Results.Text($"observe failed: unknown run_id '{runId}' (run may have already been finalised or evicted as stale)", "text/plain", statusCode: 404);

        var finalize = form["finalize"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

        // Per-step record (optional — finalize-only POSTs are valid too).
        if (int.TryParse(form["step"], out var stepNumber))
        {
            var stepPass = form["pass"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
            var detail = form["detail"].ToString();
            var label = run.Steps.FirstOrDefault(s => s.Number == stepNumber)?.Label ?? $"step {stepNumber}";
            var startedAt = ParseTs(form["started_at"].ToString());
            var endedAt = ParseTs(form["ended_at"].ToString());
            if (startedAt == default) startedAt = DateTimeOffset.UtcNow;
            if (endedAt == default) endedAt = DateTimeOffset.UtcNow;

            runStore.RecordStep(runId, new DemoStepResult(stepNumber, label, stepPass, detail, startedAt, endedAt));

            if (!finalize)
            {
                return Results.Text($"OBSERVED: run_id={runId} step={stepNumber} pass={stepPass}\n",
                    "text/plain");
            }
        }
        else if (!finalize)
        {
            return Results.Text("observe failed: provide either step=<n> or finalize=true",
                "text/plain", statusCode: 400);
        }

        // Finalise — render the report from accumulated state.
        var finalised = runStore.Finalise(runId);
        if (finalised is null)
        {
            // Race: someone else finalised. Treat as success.
            return Results.Text($"FINALISED: run_id={runId} (already)\n", "text/plain");
        }

        var stepResults = new List<DemoStepResult>();
        foreach (var s in finalised.Steps.OrderBy(s => s.Number))
        {
            if (finalised.Results.TryGetValue(s.Number, out var r))
                stepResults.Add(r);
            else
                stepResults.Add(new DemoStepResult(
                    s.Number, s.Label, false, "step not observed (agent did not POST a result)",
                    DateTimeOffset.MinValue, DateTimeOffset.MinValue));
        }

        var pass = stepResults.Count(s => s.Pass);
        var total = stepResults.Count;

        if (finalised.NoReport)
        {
            return Results.Text(
                $"FINALISED: run_id={runId} result={pass}/{total} report=skipped\nDEMO RESULT: {pass}/{total} PASS\n",
                "text/plain");
        }

        try
        {
            var input = new DemoReportInput(
                DomainName:    $"{finalised.TargetName}-{finalised.DemoName}",
                SessionId:     finalised.SessionId,
                PlanTag:       await TryReadPlanTag(collector, finalised.SessionId),
                Preflight:     finalised.Preflight,
                PreflightPass: finalised.PreflightPass,
                PreflightTotal: finalised.PreflightTotal,
                Steps:         stepResults,
                DemoStartedAt: finalised.DemoStartedAt,
                DemoEndedAt:   DateTimeOffset.UtcNow,
                TeardownText:  finalised.TeardownText);

            var path = await reportWriter.WriteAsync(input);
            return Results.Text(
                $"FINALISED: run_id={runId} result={pass}/{total} report={path}\nDEMO RESULT: {pass}/{total} PASS\n",
                "text/plain");
        }
        catch (Exception ex)
        {
            return Results.Text(
                $"FINALISED: run_id={runId} result={pass}/{total} report=failed: {ex.Message}\nDEMO RESULT: {pass}/{total} PASS\n",
                "text/plain");
        }
    }

    private static DateTimeOffset ParseTs(string s) =>
        DateTimeOffset.TryParse(s, out var v) ? v : default;

    private static async Task<string?> TryReadPlanTag(ICollectorControlClient collector, string sessionId)
    {
        try
        {
            var resp = await collector.GetSessionEnrichmentsAsync(sessionId);
            if (resp is not { StatusCode: 200 } || string.IsNullOrEmpty(resp.Body)) return null;
            const string key = "\"plan\":\"";
            var idx = resp.Body.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            var start = idx + key.Length;
            var end = resp.Body.IndexOf('"', start);
            return end > start ? resp.Body[start..end] : null;
        }
        catch { return null; }
    }
}
