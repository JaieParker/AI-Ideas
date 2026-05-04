using System.Text;
using System.Text.Json;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for /demo. Plan-23 (revised after the
/// "OTEL-only audit" constraint): the agent NEVER reports per-step
/// PASS/FAIL via a side channel. PASS/FAIL is derived exclusively
/// from <c>claude_code.skill_activated</c> events in
/// <c>output/telemetry.jsonl</c> within the run window
/// (BR-DEMO-008).
///
/// Two verbs on this endpoint:
///
/// 1. Default (no <c>finalize</c> arg). Emit a structured
///    <c>DEMO_PLAN v1</c> body listing each step as a
///    <c>STEP_INVOKE</c> / <c>STEP_OBSERVE</c> marker. Persist the
///    run-state under <c>output/demo-runs/&lt;run_id&gt;.json</c> so the
///    finalize verb can correlate. The agent invokes each chained
///    step via the Claude Code Skill tool — that produces real
///    <c>claude_code.skill_activated</c> events the collector
///    records.
///
/// 2. <c>finalize=&lt;run_id&gt;</c>. Read run-state, slice
///    <c>output/telemetry.jsonl</c> from the run's
///    <c>StartedAt</c> to now, correlate
///    <c>claude_code.skill_activated</c> events to the plan's
///    invoke steps by <c>skill.name</c>, derive PASS/FAIL per
///    step from event presence, render <c>DEMO_REPORT v1</c>.
/// </summary>
public static class DemoDispatchEndpoint
{
    private const string OutputFile                = "output/telemetry.jsonl";
    private const string PersistentEnrichmentsFile = "persistent-enrichments.json";
    private const string RunsDir                   = "output/demo-runs";
    private const string DefaultTarget             = "otel";
    private const string DefaultSession            = "JA-DEMO";

    public static IEndpointRouteBuilder MapDemoDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/demo/dispatch", Handle)
            .WithName("DemoDispatch")
            .WithSummary("Skill dispatcher for /demo — emits DEMO_PLAN v1 or DEMO_REPORT v1");
        return app;
    }

    private static async Task<IResult> Handle(
        HttpContext ctx,
        ICollectorControlClient collector,
        IPortProbe ports,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IDomainResolver domains,
        IEnumerable<IDemoTarget> demoTargets,
        IDemoReportWriter reportWriter)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        if (string.IsNullOrEmpty(sessionId)) sessionId = DefaultSession;

        var args = form["args"].ToString().Trim();
        var noReport = args.Contains("--no-report", StringComparison.Ordinal);

        // finalize=<run_id> verb — render DEMO_REPORT v1 from JSONL.
        var finalizeRunId = ParseFinalize(args);
        if (finalizeRunId is not null)
            return await HandleFinalize(finalizeRunId, reportWriter, noReport);

        // Default verb — emit plan + persist run-state.
        return await HandleEmit(args, sessionId, collector, ports, config, demoTargets);
    }

    // ---- emit ---------------------------------------------------------------

    private static async Task<IResult> HandleEmit(
        string args, string sessionId,
        ICollectorControlClient collector,
        IPortProbe ports,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IEnumerable<IDemoTarget> demoTargets)
    {
        var demoStartedAt = DateTimeOffset.UtcNow;
        var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Where(t => t != "--no-report" && !t.StartsWith("finalize=", StringComparison.Ordinal))
                         .ToArray();
        var targetName = tokens.Length == 0 ? DefaultTarget : tokens[0];
        var demoName = tokens.Length >= 2 ? tokens[1] : null;

        var target = demoTargets.FirstOrDefault(t => t.TargetName == targetName);
        if (target is null)
        {
            var known = string.Join(", ", demoTargets.Select(t => t.TargetName));
            return Results.Text(
                $"demo failed: unknown target '{targetName}' (known: {(string.IsNullOrEmpty(known) ? "(none)" : known)})",
                "text/plain");
        }

        // BR-CODE-001 + BR-OTEL-007 — OTLP port from typed options.
        var otlpHttpPort = config.GetValue("Otel:CollectorOtlpPort",
            ComponentRegistry.DefaultCollectorOtlpPort);

        var sb = new StringBuilder();
        sb.AppendLine($"=== /demo — guided tour of the {target.TargetName} {target.TargetKind} ===");
        sb.AppendLine();
        sb.AppendLine("/demo emits a structured plan; the live agent turn invokes each chained step");
        sb.AppendLine("via the Skill tool. Every chained skill traverses the real Claude Code harness,");
        sb.AppendLine("emitting `claude_code.skill_activated` events into output/telemetry.jsonl —");
        sb.AppendLine("that is the integration-test signal. After the chain runs, the agent calls");
        sb.AppendLine("this same endpoint with `args=<target> finalize=<run_id>` to render the");
        sb.AppendLine("DEMO_REPORT v1; PASS/FAIL is derived exclusively from the JSONL events");
        sb.AppendLine("(BR-DEMO-008 — no agent-side accumulation, no side channel).");
        sb.AppendLine();

        // ---- pre-flight ----
        sb.AppendLine("PRE-FLIGHT");
        sb.AppendLine("==========");

        var preflight = new List<(string Id, bool Pass, string Detail, string? Fix)>();

        preflight.Add(("00.a", true,
            "Helpers sidecar (you are reading this from it on :5050)",
            null));

        var collectorUp = await collector.IsHealthyAsync();
        preflight.Add(("00.b", collectorUp,
            collectorUp ? "Collector control reachable on :13133" : "Collector control NOT reachable on :13133",
            collectorUp ? null : "build + run the Go collector — see The-OTEL-Plan-2-go-collector.md (or use /otel up)"));

        var outputDirOk = TryEnsureWriteable(Path.GetDirectoryName(OutputFile) ?? "output");
        preflight.Add(("00.c", outputDirOk,
            outputDirOk ? $"Output dir writeable ({OutputFile})" : "Output dir NOT writeable",
            outputDirOk ? null : "ensure the project root is writeable by the current user"));

        var persistentFileOk = File.Exists(PersistentEnrichmentsFile)
                            || CanCreate(PersistentEnrichmentsFile);
        preflight.Add(("00.d", persistentFileOk,
            persistentFileOk
                ? $"Persistent-enrichments file present or creatable ({PersistentEnrichmentsFile})"
                : "Persistent-enrichments file NOT writeable",
            persistentFileOk ? null : "ensure the project root is writeable; the collector will create it on first set"));

        var otlpPortHeld = ports.IsListening(otlpHttpPort);
        var otlpPortOk = !otlpPortHeld || collectorUp;
        preflight.Add(("00.e", otlpPortOk,
            otlpPortOk
                ? (otlpPortHeld
                    ? $"OTLP port :{otlpHttpPort} owned by project collector"
                    : $"OTLP port :{otlpHttpPort} free (collector can bind)")
                : $"OTLP port :{otlpHttpPort} CONFLICT — held by another process, project collector cannot bind",
            otlpPortOk ? null : $"another OTLP receiver owns :{otlpHttpPort}; either stop it OR re-port the project collector (see HOW TO BRING IT UP below)"));

        var preflightPass = preflight.Count(p => p.Pass);
        var preflightTotal = preflight.Count;

        foreach (var p in preflight)
        {
            var status = p.Pass ? "PASS" : "FAIL";
            sb.AppendLine($"STEP {p.Id}: {status} — {p.Detail}");
            if (!p.Pass && p.Fix is not null)
                sb.AppendLine($"           fix: {p.Fix}");
        }
        sb.AppendLine();
        sb.AppendLine($"PRE-FLIGHT RESULT: {preflightPass}/{preflightTotal} PASS");
        sb.AppendLine();

        // BR-SKILL-014 — RECOVERY_AVAILABLE marker.
        if (!collectorUp && otlpPortOk && !otlpPortHeld)
        {
            sb.AppendLine($"RECOVERY_AVAILABLE v1: skill=\"otel\" verb=\"up\" reason=\"collector control down on :13133; OTLP port :{otlpHttpPort} is free\"");
            sb.AppendLine();
        }

        if (!otlpPortOk)
        {
            sb.AppendLine("HOW TO BRING IT UP");
            sb.AppendLine("==================");
            sb.AppendLine($"PORT CONFLICT — :{otlpHttpPort} is already held by another process.");
            sb.AppendLine("The project collector cannot bind until the conflict is resolved.");
            sb.AppendLine("Choose one of these recoveries (BR-OTEL-005):");
            sb.AppendLine();
            sb.AppendLine("  Option A — stop the holder (you decide; nothing on your machine is");
            sb.AppendLine("             auto-killed by skills per BR-SECURITY-003):");
            sb.AppendLine($"               PowerShell:  Get-NetTCPConnection -LocalPort {otlpHttpPort} -State Listen | Stop-Process -Id {{$_.OwningProcess}} -Force");
            sb.AppendLine($"               (or just close the application that owns :{otlpHttpPort})");
            sb.AppendLine();
            sb.AppendLine("  Option B — re-port the project collector to a free port (BR-OTEL-007 single source of truth):");
            sb.AppendLine($"               Edit src/HelpersSidecar/appsettings.Local.json (gitignored) — set");
            sb.AppendLine($"               Otel:CollectorOtlpPort to a free port (e.g. 14318), then restart the");
            sb.AppendLine($"               sidecar so it picks up the new value. The sidecar exports");
            sb.AppendLine($"               CLAUDE_OTEL_OTLP_HTTP_PORT to the spawned Go collector; config.yaml");
            sb.AppendLine($"               consumes the env var via OTel-native substitution. One file edit");
            sb.AppendLine($"               moves both halves (sidecar's port-probe target AND collector's bind).");
            sb.AppendLine($"               Note: Claude Code's OTLP exporter targets :{otlpHttpPort} by default,");
            sb.AppendLine("               so re-porting means real Claude Code traces will not reach this");
            sb.AppendLine($"               collector unless you also reconfigure CLAUDE_CODE_OTLP_ENDPOINT.");
            sb.AppendLine();
            sb.AppendLine($"DEMO RESULT: SKIPPED (port conflict on :{otlpHttpPort})");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        // ---- resolve case + persist run-state + emit plan ----
        var defaultCase = target.Demos.FirstOrDefault(c => c.IsDefault);
        var demoCase = demoName is null
            ? defaultCase
            : target.Demos.FirstOrDefault(c => c.Name == demoName);
        if (demoCase is null)
        {
            var availableNames = string.Join(", ", target.Demos.Select(d => d.Name));
            var msg = demoName is null
                ? $"no IsDefault=true demo case registered for target '{target.TargetName}'"
                : $"unknown demo '{demoName}' for target '{target.TargetName}' (available: {availableNames})";
            sb.AppendLine($"DEMO_UNKNOWN v1: target=\"{target.TargetName}\" demo=\"{demoName ?? string.Empty}\" reason=\"{msg}\"");
            sb.AppendLine();
            sb.AppendLine("DEMO RESULT: SKIPPED (no demo case)");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        var steps = (await demoCase.Plan(new DemoContext(sessionId), CancellationToken.None)).ToArray();
        var runId = Guid.NewGuid().ToString("N");

        // Persist run-state for finalize.
        await WriteRunStateAsync(runId, target.TargetName, target.TargetKind,
            demoCase.Name, demoCase.Description, sessionId, demoStartedAt, steps,
            preflight.Select(p => new DemoPreflightRow(p.Id, p.Pass, p.Detail, p.Fix)).ToList(),
            preflightPass, preflightTotal, TeardownText());

        sb.AppendLine($"DEMO_PLAN v1: target=\"{target.TargetName}\" target_kind=\"{target.TargetKind}\" demo=\"{demoCase.Name}\" steps={steps.Length} run_id=\"{runId}\"");
        sb.AppendLine($"DEMO_DESCRIPTION: {demoCase.Description}");
        sb.AppendLine();
        sb.AppendLine("LIVE PLAN — execute each STEP_INVOKE in order via the Skill tool. For");
        sb.AppendLine("STEP_OBSERVE rows, the agent reads the named file via the Read tool and");
        sb.AppendLine("prints a one-line summary (still derived from OTEL output, never agent");
        sb.AppendLine("self-report). After the last step, call this same endpoint with");
        sb.AppendLine($"`args={target.TargetName} finalize={runId}` to render DEMO_REPORT v1.");
        sb.AppendLine();

        foreach (var s in steps)
        {
            if (s.Kind == "observe")
            {
                sb.AppendLine($"STEP_OBSERVE: number={s.Number} target=\"{s.ObserveTarget}\" label=\"{Escape(s.Label)}\"");
            }
            else
            {
                var expectClause = string.IsNullOrEmpty(s.Expect) ? string.Empty : $" expect=\"{Escape(s.Expect)}\"";
                sb.AppendLine($"STEP_INVOKE: number={s.Number} skill=\"{s.Skill}\" args=\"{Escape(s.Args)}\" label=\"{Escape(s.Label)}\"{expectClause}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("After every step has been invoked, render the report:");
        sb.AppendLine($"  curl http://127.0.0.1:5050/skills/demo/dispatch -sS --data-urlencode 'args={target.TargetName} finalize={runId}'");
        sb.AppendLine();
        AppendTeardownSection(sb);

        return Results.Text(sb.ToString(), "text/plain");
    }

    // ---- finalize -----------------------------------------------------------

    private static async Task<IResult> HandleFinalize(string runId, IDemoReportWriter reportWriter, bool noReport)
    {
        var run = await ReadRunStateAsync(runId);
        if (run is null)
            return Results.Text(
                $"finalize failed: unknown run_id '{runId}' (run-state file missing under {RunsDir}/)",
                "text/plain", statusCode: 404);

        var endedAt = DateTimeOffset.UtcNow;
        var jsonl = new JsonlSliceReader(OutputFile);
        var slice = jsonl.ReadSlice(new JsonlSliceFilter(
            StartedAt: run.StartedAt,
            EndedAt:   endedAt,
            SessionId: run.SessionId));

        // Correlate: for each STEP_INVOKE, look for any record whose RawLine
        // contains both `claude_code.skill_activated` (the event name the
        // Claude Code harness emits) AND the matching `skill.name` value.
        // For STEP_OBSERVE, just summarise the read-only count.
        var stepResults = new List<DemoStepResult>();
        foreach (var step in run.Steps.OrderBy(s => s.Number))
        {
            if (step.Kind == "observe")
            {
                var fileExists = File.Exists(step.ObserveTarget);
                var byteCount = fileExists ? new FileInfo(step.ObserveTarget).Length : 0L;
                var lineCount = slice.Count;
                var detail = fileExists
                    ? $"{step.ObserveTarget} present ({byteCount} bytes); {lineCount} session-scoped JSONL records in run window"
                    : $"{step.ObserveTarget} does not exist yet (collector hasn't flushed)";
                stepResults.Add(new DemoStepResult(step.Number, step.Label, fileExists, detail,
                    run.StartedAt, endedAt));
                continue;
            }

            var skillName = step.Skill;
            var matching = slice.Where(r =>
                r.RawLine.Contains("\"claude_code.skill_activated\"", StringComparison.Ordinal) &&
                ContainsSkillName(r.RawLine, skillName)).ToArray();

            var pass = matching.Length > 0;
            var detailMsg = pass
                ? $"chain → /{skillName} {step.Args} — claude_code.skill_activated event found in JSONL ({matching.Length} record(s))"
                : $"chain → /{skillName} {step.Args} — NO claude_code.skill_activated event with skill.name=\"{skillName}\" found in JSONL run window (BR-DEMO-008: no event = FAIL)";
            stepResults.Add(new DemoStepResult(step.Number, step.Label, pass, detailMsg,
                run.StartedAt, endedAt));
        }

        var passCount = stepResults.Count(s => s.Pass);
        var total = stepResults.Count;

        var sb = new StringBuilder();
        sb.AppendLine($"=== /demo finalize — {run.TargetName}/{run.DemoName} ===");
        sb.AppendLine();
        sb.AppendLine($"DEMO_FINALIZE v1: target=\"{run.TargetName}\" demo=\"{run.DemoName}\" run_id=\"{runId}\" started_at=\"{run.StartedAt:O}\" ended_at=\"{endedAt:O}\" jsonl_records={slice.Count}");
        sb.AppendLine();

        foreach (var s in stepResults)
        {
            var status = s.Pass ? "PASS" : "FAIL";
            sb.AppendLine($"STEP {s.Number:00}: {status} — {s.Label}");
            if (!string.IsNullOrWhiteSpace(s.Detail))
                sb.AppendLine($"          {s.Detail}");
        }

        sb.AppendLine();
        sb.AppendLine($"DEMO RESULT: {passCount}/{total} PASS");
        sb.AppendLine();

        if (noReport)
        {
            sb.AppendLine("Report rendering skipped (--no-report).");
            return Results.Text(sb.ToString(), "text/plain");
        }

        try
        {
            var input = new DemoReportInput(
                DomainName:    $"{run.TargetName}-{run.DemoName}",
                SessionId:     run.SessionId,
                PlanTag:       null,
                Preflight:     run.Preflight,
                PreflightPass: run.PreflightPass,
                PreflightTotal: run.PreflightTotal,
                Steps:         stepResults,
                DemoStartedAt: run.StartedAt,
                DemoEndedAt:   endedAt,
                TeardownText:  run.TeardownText);
            var path = await reportWriter.WriteAsync(input);
            sb.AppendLine($"Report saved to: {path}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Report write failed: {ex.Message}");
        }

        // Best-effort: delete the run-state file once finalised.
        try { File.Delete(RunStatePath(runId)); } catch { /* tolerate */ }

        return Results.Text(sb.ToString(), "text/plain");
    }

    // ---- run-state persistence ---------------------------------------------

    private static string RunStatePath(string runId) => Path.Combine(RunsDir, $"{runId}.json");

    private static async Task WriteRunStateAsync(
        string runId, string targetName, string targetKind, string demoName, string demoDescription,
        string sessionId, DateTimeOffset startedAt,
        IReadOnlyList<DemoStepDescriptor> steps,
        IReadOnlyList<DemoPreflightRow> preflight, int preflightPass, int preflightTotal,
        string teardownText)
    {
        Directory.CreateDirectory(RunsDir);
        var record = new RunStateRecord(
            RunId: runId,
            TargetName: targetName,
            TargetKind: targetKind,
            DemoName: demoName,
            DemoDescription: demoDescription,
            SessionId: sessionId,
            StartedAt: startedAt,
            Steps: steps,
            Preflight: preflight,
            PreflightPass: preflightPass,
            PreflightTotal: preflightTotal,
            TeardownText: teardownText);
        await File.WriteAllTextAsync(RunStatePath(runId),
            JsonSerializer.Serialize(record, RunStateJsonOptions));
    }

    private static async Task<RunStateRecord?> ReadRunStateAsync(string runId)
    {
        var path = RunStatePath(runId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<RunStateRecord>(json, RunStateJsonOptions);
    }

    private static readonly JsonSerializerOptions RunStateJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    private sealed record RunStateRecord(
        string RunId,
        string TargetName,
        string TargetKind,
        string DemoName,
        string DemoDescription,
        string SessionId,
        DateTimeOffset StartedAt,
        IReadOnlyList<DemoStepDescriptor> Steps,
        IReadOnlyList<DemoPreflightRow> Preflight,
        int PreflightPass,
        int PreflightTotal,
        string TeardownText);

    // ---- helpers ------------------------------------------------------------

    private static string? ParseFinalize(string args)
    {
        // Token like `finalize=<run_id>` may appear anywhere in args.
        foreach (var tok in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok.StartsWith("finalize=", StringComparison.Ordinal))
            {
                var v = tok["finalize=".Length..];
                return string.IsNullOrEmpty(v) ? null : v;
            }
        }
        return null;
    }

    private static bool ContainsSkillName(string rawLine, string skillName)
    {
        // The harness emits skill.name as either a top-level attribute
        // or a body attribute on a logRecord. Be permissive: match
        // `"skill.name"` adjacent (with JSON wrapper noise) to a
        // `"stringValue":"<skillName>"`. Cheaper than a full parse and
        // resilient to the OTLP wrapping shape variations.
        var nameKey = "\"skill.name\"";
        var idx = rawLine.IndexOf(nameKey, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var window = rawLine.Substring(idx, Math.Min(rawLine.Length - idx, 256));
            var valueMarker = $"\"stringValue\":\"{skillName}\"";
            if (window.Contains(valueMarker, StringComparison.Ordinal)) return true;
            idx = rawLine.IndexOf(nameKey, idx + 1, StringComparison.Ordinal);
        }
        return false;
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"");

    private static string TeardownText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("To stop the OTEL tenant (collector):");
        sb.AppendLine("  /otel down");
        sb.AppendLine();
        sb.AppendLine("To stop the deterministic-helpers platform (sidecar):");
        sb.AppendLine("  /skill-bootstrap stop");
        sb.AppendLine();
        sb.AppendLine("Re-run /demo any time to see the off-state again. The system is");
        sb.AppendLine("fully reversible — no machine state survives stop except the");
        sb.AppendLine($"{PersistentEnrichmentsFile} file, which can be cleared with");
        sb.AppendLine("`/otel config clear`.");
        return sb.ToString();
    }

    private static void AppendTeardownSection(StringBuilder sb)
    {
        sb.AppendLine("TEARDOWN (optional — for demo reversibility)");
        sb.AppendLine("============================================");
        sb.Append(TeardownText());
    }

    private static bool TryEnsureWriteable(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static bool CanCreate(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) return false;
            return true;
        }
        catch { return false; }
    }
}
