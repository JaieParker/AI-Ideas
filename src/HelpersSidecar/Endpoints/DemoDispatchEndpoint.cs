using System.Text;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /demo skill — a guided onboarding
/// tour AND the project's full-stack integration test surface.
///
/// Plan-5 Phase 2d: this endpoint is now a thin executor. The
/// platform-level pre-flight (sidecar / collector control /
/// output dir / persistent file / OTLP port — STEP 00.x rows)
/// and the teardown section live here. The live skill-chain
/// section is owned by the resolved domain's
/// <see cref="IDomainDemo"/> (BR-EXTEND-010); domains without an
/// IDomainDemo render with no live steps and a "no demo for
/// domain X" notice.
///
/// /demo args shape: <c>[&lt;domain&gt;]</c> — domain optional, defaults
/// to "otel". /demo is OTEL-flavoured by history; future domains
/// pass their name explicitly.
/// </summary>
public static class DemoDispatchEndpoint
{
    private const string OutputFile    = "output/telemetry.jsonl";
    private const string PersistentEnrichmentsFile = "persistent-enrichments.json";
    private const string DefaultDomain = "otel";
    private const string DefaultSession = "JA-DEMO";

    public static IEndpointRouteBuilder MapDemoDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/demo/dispatch", Handle)
            .WithName("DemoDispatch")
            .WithSummary("Skill dispatcher for /demo — guided tour + skill-chain integration test");
        return app;
    }

    private static async Task<IResult> Handle(
        HttpContext ctx,
        ICollectorControlClient collector,
        ISkillDispatchClient skills,
        IPortProbe ports,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IDomainResolver domains,
        IEnumerable<IDomainDemo> demos,
        IDemoReportWriter reportWriter)
    {
        var demoStartedAt = DateTimeOffset.UtcNow;
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        if (string.IsNullOrEmpty(sessionId)) sessionId = DefaultSession;

        // Parse args: first token = domain (default 'otel'). --no-report
        // anywhere in args opts out of BR-DEMO-004 report generation.
        var args = form["args"].ToString().Trim();
        var noReport = args.Contains("--no-report", StringComparison.Ordinal);
        var firstToken = string.IsNullOrEmpty(args)
            ? string.Empty
            : args.Split(' ', 2)[0];
        var domainName = string.IsNullOrEmpty(firstToken) || firstToken == "--no-report"
            ? DefaultDomain
            : firstToken;

        if (!domains.TryResolve(domainName, out var domain))
            return Results.Text(
                $"demo failed: unknown domain '{domainName}' (known: {string.Join(", ", domains.KnownNames)})",
                "text/plain");

        // BR-CODE-001 + BR-OTEL-007 — the OTLP port is a setting,
        // resolved from the same typed option that drives the
        // collector spawn's CLAUDE_OTEL_OTLP_HTTP_PORT env var.
        // Single source of truth; pre-flight probe and collector's
        // bind target stay in sync.
        var otlpHttpPort = config.GetValue("Otel:CollectorOtlpPort",
            ComponentRegistry.DefaultCollectorOtlpPort);

        var sb = new StringBuilder();
        sb.AppendLine($"=== /demo — guided tour of the {domain!.Name} domain ===");
        sb.AppendLine();
        sb.AppendLine("/demo is a pure skill-chain orchestrator. Every action step");
        sb.AppendLine("below invokes another skill via the sidecar's dispatch loopback,");
        sb.AppendLine("never the collector or vendor APIs directly. That makes /demo");
        sb.AppendLine("both a demonstration of skill chaining AND a full-stack");
        sb.AppendLine("integration test — running it end-to-end exercises the entire");
        sb.AppendLine("skill stack (parsing, validation, collector contract, exporters).");
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

        // BR-SKILL-014 — emit a structured RECOVERY_AVAILABLE v1
        // marker when a project skill can recover the down-state.
        // Today the only auto-recoverable case is "collector control
        // down + port free" → /otel up. NEVER emit when port is held
        // by another process (BR-SECURITY-003 — we don't recommend
        // stopping a process we don't own); /demo's existing port-
        // conflict messaging covers that case via Option A / Option B.
        if (!collectorUp && otlpPortOk && !otlpPortHeld)
        {
            sb.AppendLine($"RECOVERY_AVAILABLE v1: skill=\"otel\" verb=\"up\" reason=\"collector control down on :13133; OTLP port :{otlpHttpPort} is free\"");
            sb.AppendLine();
        }

        // Skip-live branch: only when there's a true OTLP port conflict
        // (BR-OTEL-005). In every other state, the live demo runs.
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
            sb.AppendLine("  Option B — re-port the project collector to a free port:");
            sb.AppendLine($"               Edit config.yaml — change `otlp.protocols.http.endpoint` from");
            sb.AppendLine($"               127.0.0.1:{otlpHttpPort} to a free port (e.g. 14318), then start.");
            sb.AppendLine($"               Note: Claude Code's OTLP exporter targets :{otlpHttpPort} by default,");
            sb.AppendLine("               so re-porting means real Claude Code traces will not reach this");
            sb.AppendLine($"               collector unless you also reconfigure CLAUDE_CODE_OTLP_ENDPOINT.");
            sb.AppendLine();
            sb.AppendLine("Re-run /demo once the conflict is resolved. The live demo's first step");
            sb.AppendLine("(/otel up) will spawn the collector automatically; no manual command needed.");
            sb.AppendLine();
            sb.AppendLine($"DEMO RESULT: SKIPPED (port conflict on :{otlpHttpPort})");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        // ============================================================
        // LIVE DEMO STEPS — owned by the resolved domain's IDomainDemo
        // (BR-EXTEND-010). If the domain didn't register one, render
        // a "no demo" notice and fall through to teardown.
        // ============================================================
        var demo = demos.FirstOrDefault(d => d.DomainName == domain.Name);
        if (demo is null)
        {
            sb.AppendLine($"NO DEMO REGISTERED for domain '{domain.Name}'");
            sb.AppendLine("================");
            sb.AppendLine("This domain has not registered an IDomainDemo. Per BR-EXTEND-010,");
            sb.AppendLine("a demo is the canonical onboarding path; consider adding one.");
            sb.AppendLine();
            sb.AppendLine("DEMO RESULT: SKIPPED (no IDomainDemo for this domain)");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        sb.AppendLine("LIVE DEMO STEPS (each step invokes another skill — pure chain)");
        sb.AppendLine("==============================================================");

        var demoCtx = new DemoContext(sessionId, skills);
        var steps = await demo.RunAsync(demoCtx);

        var stepsPass = steps.Count(s => s.Pass);
        var stepsTotal = steps.Count;

        foreach (var s in steps)
        {
            var status = s.Pass ? "PASS" : "FAIL";
            sb.AppendLine($"STEP {s.Number:00}: {status} — {s.Label}");
            if (!string.IsNullOrWhiteSpace(s.Detail))
                sb.AppendLine($"          {s.Detail}");
        }

        sb.AppendLine();
        sb.AppendLine($"DEMO RESULT: {stepsPass}/{stepsTotal} PASS");
        sb.AppendLine();
        AppendTeardownSection(sb);

        // BR-DEMO-004 — write the durable report unless --no-report.
        if (!noReport)
        {
            var reportInput = new DemoReportInput(
                DomainName: domain.Name,
                SessionId: sessionId,
                PlanTag: await TryReadPlanTag(collector, sessionId),
                Preflight: preflight.Select(p => new DemoPreflightRow(p.Id, p.Pass, p.Detail, p.Fix)).ToList(),
                PreflightPass: preflightPass,
                PreflightTotal: preflightTotal,
                Steps: steps,
                DemoStartedAt: demoStartedAt,
                DemoEndedAt: DateTimeOffset.UtcNow,
                TeardownText: TeardownText());
            try
            {
                var path = await reportWriter.WriteAsync(reportInput);
                sb.AppendLine();
                sb.AppendLine($"Report saved to: {path}");
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine($"Report not written: {ex.Message}");
            }
        }

        return Results.Text(sb.ToString(), "text/plain");
    }

    private static async Task<string?> TryReadPlanTag(ICollectorControlClient collector, string sessionId)
    {
        try
        {
            var resp = await collector.GetSessionEnrichmentsAsync(sessionId);
            if (resp is not { StatusCode: 200 } || string.IsNullOrEmpty(resp.Body)) return null;
            // Body is a JSON object { "plan": "...", ... }. Use a simple
            // string match instead of a JSON parse — robust against any
            // shape the collector returns and avoids a hard dependency.
            const string key = "\"plan\":\"";
            var idx = resp.Body.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            var start = idx + key.Length;
            var end = resp.Body.IndexOf('"', start);
            return end > start ? resp.Body[start..end] : null;
        }
        catch { return null; }
    }

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
