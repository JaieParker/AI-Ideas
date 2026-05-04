using System.Text;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for /demo. Plan-23 inverted the contract:
/// this endpoint NO LONGER chains to other skills via in-process
/// HTTP loopback (the old <c>ISkillDispatchClient</c> path —
/// retired). Instead, the response body carries a
/// <c>DEMO_PLAN v1</c> header followed by numbered
/// <c>STEP_INVOKE</c> markers. The /demo SKILL.md body iterates
/// each marker and invokes the named skill via the Claude Code
/// Skill tool, producing <c>claude_code.skill_activated</c>
/// events that the collector records — the integration-test
/// surface BR-DEMO-001 / BR-DEMO-002 promised but never delivered
/// pre-Plan-23 (false-green incident, 2026-05-04).
///
/// The endpoint still owns the platform-level pre-flight
/// (sidecar / collector / output dir / persistent file / OTLP
/// port — STEP 00.x rows) and renders the teardown text. The
/// live skill-chain section is now markers, not rows.
/// </summary>
public static class DemoDispatchEndpoint
{
    private const string OutputFile                = "output/telemetry.jsonl";
    private const string PersistentEnrichmentsFile = "persistent-enrichments.json";
    private const string DefaultTarget             = "otel";
    private const string DefaultSession            = "JA-DEMO";

    public static IEndpointRouteBuilder MapDemoDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/demo/dispatch", Handle)
            .WithName("DemoDispatch")
            .WithSummary("Skill dispatcher for /demo — emits a DEMO_PLAN v1 the agent executes via Skill");
        return app;
    }

    private static async Task<IResult> Handle(
        HttpContext ctx,
        ICollectorControlClient collector,
        IPortProbe ports,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IDomainResolver domains,
        IEnumerable<IDemoTarget> demoTargets,
        IDemoRunStore runStore)
    {
        var demoStartedAt = DateTimeOffset.UtcNow;
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        if (string.IsNullOrEmpty(sessionId)) sessionId = DefaultSession;

        // Args: <target> [<demo>]; --no-report opts out of report write.
        var args = form["args"].ToString().Trim();
        var noReport = args.Contains("--no-report", StringComparison.Ordinal);
        var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Where(t => t != "--no-report").ToArray();
        var targetName = tokens.Length == 0 ? DefaultTarget : tokens[0];
        var demoName = tokens.Length >= 2 ? tokens[1] : null;

        var target = demoTargets.FirstOrDefault(t => t.TargetName == targetName);
        if (target is null)
        {
            // For the "domain" branch we still want a usable error that
            // names every registered target; skill-level demos land later.
            var known = string.Join(", ", demoTargets.Select(t => t.TargetName));
            var fallbackMsg =
                $"demo failed: unknown target '{targetName}' (known: {(string.IsNullOrEmpty(known) ? "(none)" : known)})";
            return Results.Text(fallbackMsg, "text/plain");
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
        sb.AppendLine("that is what makes /demo simultaneously a guided tour AND an integration test");
        sb.AppendLine("(BR-DEMO-001 / BR-DEMO-002 amended).");
        sb.AppendLine();

        // ============================================================
        // PRE-FLIGHT (00.x rows). Platform-level — never delegated.
        // ============================================================
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

        // BR-SKILL-014 — emit RECOVERY_AVAILABLE v1 when /otel up can
        // recover the down-state. Suppressed when port held by another
        // process (BR-SECURITY-003 — never recommend stopping foreign
        // processes).
        if (!collectorUp && otlpPortOk && !otlpPortHeld)
        {
            sb.AppendLine($"RECOVERY_AVAILABLE v1: skill=\"otel\" verb=\"up\" reason=\"collector control down on :13133; OTLP port :{otlpHttpPort} is free\"");
            sb.AppendLine();
        }

        // Skip-live branch: only on a true OTLP port conflict.
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
            sb.AppendLine("Re-run /demo once the conflict is resolved. The plan's first step (/otel up)");
            sb.AppendLine("will spawn the collector automatically; no manual command needed.");
            sb.AppendLine();
            sb.AppendLine($"DEMO RESULT: SKIPPED (port conflict on :{otlpHttpPort})");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        // ============================================================
        // RESOLVE CASE + EMIT PLAN. The body of /demo will iterate the
        // STEP_INVOKE markers and invoke each via the Skill tool.
        // ============================================================
        var defaultCase = target.Demos.FirstOrDefault(c => c.IsDefault);
        var demoCase = demoName is null
            ? defaultCase
            : target.Demos.FirstOrDefault(c => c.Name == demoName);
        if (demoCase is null)
        {
            var availableNames = string.Join(", ", target.Demos.Select(d => d.Name));
            var msg = demoName is null
                ? $"no IsDefault=true demo case registered for target '{target.TargetName}' (available: {(string.IsNullOrEmpty(availableNames) ? "(none)" : availableNames)})"
                : $"unknown demo '{demoName}' for target '{target.TargetName}' (available: {availableNames})";
            sb.AppendLine($"DEMO_UNKNOWN v1: target=\"{target.TargetName}\" demo=\"{demoName ?? string.Empty}\" reason=\"{msg}\"");
            sb.AppendLine();
            sb.AppendLine("DEMO RESULT: SKIPPED (no demo case)");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        var steps = (await demoCase.Plan(new DemoContext(sessionId), CancellationToken.None)).ToArray();

        // Register the run so DemoObserveEndpoint can finalise the
        // report once every step's result arrives.
        var runId = Guid.NewGuid().ToString("N");
        runStore.Register(new DemoRunInputs(
            RunId:         runId,
            SessionId:     sessionId,
            TargetName:    target.TargetName,
            TargetKind:    target.TargetKind,
            DemoName:      demoCase.Name,
            DemoStartedAt: demoStartedAt,
            Steps:         steps,
            Preflight:     preflight.Select(p => new DemoPreflightRow(p.Id, p.Pass, p.Detail, p.Fix)).ToList(),
            PreflightPass: preflightPass,
            PreflightTotal: preflightTotal,
            TeardownText:  TeardownText(),
            NoReport:      noReport));

        // ---- DEMO_PLAN v1 marker section -----------------------------
        sb.AppendLine($"DEMO_PLAN v1: target=\"{target.TargetName}\" target_kind=\"{target.TargetKind}\" demo=\"{demoCase.Name}\" steps={steps.Length} run_id=\"{runId}\"");
        sb.AppendLine($"DEMO_DESCRIPTION: {demoCase.Description}");
        sb.AppendLine();
        sb.AppendLine("LIVE PLAN — execute each STEP_INVOKE in order via the Skill tool, then POST");
        sb.AppendLine("the per-step result to /skills/demo/observe (run_id, step, pass, detail).");
        sb.AppendLine("Each Skill invocation produces a claude_code.skill_activated event the");
        sb.AppendLine("collector records — that is the integration-test signal.");
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
        sb.AppendLine("After the last STEP, the agent's final action is:");
        sb.AppendLine($"  curl http://127.0.0.1:5050/skills/demo/observe -sS --data-urlencode 'run_id={runId}' --data-urlencode 'finalize=true'");
        sb.AppendLine("That POST flushes the DEMO_REPORT v1 markdown to output/demo-reports/.");
        sb.AppendLine();
        AppendTeardownSection(sb);

        return Results.Text(sb.ToString(), "text/plain");
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
