using System.Text;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /demo skill — a guided onboarding
/// tour AND the project's full-stack integration test surface.
///
/// /demo is a pure skill-chain orchestrator (BR-DEMO-002). It
/// never calls the collector control API directly, never makes
/// vendor HTTP calls (e.g. wttr.in) directly, and never sends
/// raw OTLP — everything happens through other skills'
/// dispatch endpoints via <see cref="ISkillDispatchClient"/>.
/// This makes /demo simultaneously:
///
///   1. A demonstration of skill chaining (every action goes
///      through /otel, /enrich, or /weather).
///   2. A full-stack integration test — exercising the entire
///      skill dispatch path including parsing, validation, and
///      the underlying collector contract.
///
/// The pre-flight section is the only place this endpoint reads
/// platform state directly, and only to report it: it probes
/// the collector control client's IsHealthy flag (status check,
/// not action) and the local filesystem (output dir + persistent
/// file presence — observation, not action). The JSONL summary
/// steps in the live section are read-only verification of the
/// records produced by upstream skill calls — observation, not
/// action.
/// </summary>
public static class DemoDispatchEndpoint
{
    private const string OutputFile    = "output/telemetry.jsonl";
    private const string PersistentEnrichmentsFile = "persistent-enrichments.json";
    private const string DemoSession   = "JA-DEMO";
    private const int    DefaultOtlpHttpPort = 4318;

    public static IEndpointRouteBuilder MapDemoDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/demo/dispatch", Handle)
            .WithName("DemoDispatch")
            .WithSummary("Skill dispatcher for /demo — guided tour + skill-chain integration test");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, ICollectorControlClient collector, ISkillDispatchClient skills, IPortProbe ports, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        if (string.IsNullOrEmpty(sessionId)) sessionId = DemoSession;

        // BR-CODE-001 — the OTLP port is a setting, not a constant. Default
        // 4318 (canonical OTLP/HTTP); dev environments may re-port via
        // appsettings.Development.json (e.g. 14318 when another local OTLP
        // receiver owns 4318).
        var otlpHttpPort = config.GetValue("Otel:CollectorOtlpPort", DefaultOtlpHttpPort);

        var sb = new StringBuilder();

        sb.AppendLine("=== /demo — guided tour of the OTEL project ===");
        sb.AppendLine();
        sb.AppendLine("/demo is a pure skill-chain orchestrator. Every action step");
        sb.AppendLine("below invokes another skill via the sidecar's dispatch loopback,");
        sb.AppendLine("never the collector or vendor APIs directly. That makes /demo");
        sb.AppendLine("both a demonstration of skill chaining AND a full-stack");
        sb.AppendLine("integration test — running it end-to-end exercises the entire");
        sb.AppendLine("skill stack (parsing, validation, collector contract, exporters).");
        sb.AppendLine();

        // ============================================================
        // PRE-FLIGHT (00.x rows). Status checks only — no actions.
        // ============================================================
        sb.AppendLine("PRE-FLIGHT");
        sb.AppendLine("==========");

        var preflight = new List<(string Id, bool Pass, string Detail, string? Fix)>();

        // 00.a — sidecar reachable. Implicit PASS: this dispatch is running
        //        inside it. Reported anyway so the table reads honestly.
        preflight.Add(("00.a", true,
            "Helpers sidecar (you are reading this from it on :5050)",
            null));

        // 00.b — collector control reachable.
        var collectorUp = await collector.IsHealthyAsync();
        preflight.Add(("00.b", collectorUp,
            collectorUp ? "Collector control reachable on :13133" : "Collector control NOT reachable on :13133",
            collectorUp ? null : "build + run the Go collector — see The-OTEL-Plan-2-go-collector.md (or wait for /otel up after Plan-5)"));

        // 00.c — output dir writeable.
        var outputDirOk = TryEnsureWriteable(Path.GetDirectoryName(OutputFile) ?? "output");
        preflight.Add(("00.c", outputDirOk,
            outputDirOk ? $"Output dir writeable ({OutputFile})" : "Output dir NOT writeable",
            outputDirOk ? null : "ensure the project root is writeable by the current user"));

        // 00.d — persistent file present or creatable.
        var persistentFileOk = File.Exists(PersistentEnrichmentsFile)
                            || CanCreate(PersistentEnrichmentsFile);
        preflight.Add(("00.d", persistentFileOk,
            persistentFileOk
                ? $"Persistent-enrichments file present or creatable ({PersistentEnrichmentsFile})"
                : "Persistent-enrichments file NOT writeable",
            persistentFileOk ? null : "ensure the project root is writeable; the collector will create it on first set"));

        // 00.e — OTLP receive port :4318 free OR owned by our collector
        //        (BR-OTEL-005). PASS if the port is free (the collector
        //        will bind on start) OR if the collector control on
        //        :13133 is reachable (so :4318 belongs to us).
        //        FAIL if :4318 is listening but :13133 is unreachable —
        //        another OTLP receiver owns the port and our collector
        //        cannot bind.
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

        // The live demo skips ONLY when there's a true OTLP port conflict
        // (BR-OTEL-005, 00.e FAIL). In every other state, the live demo
        // runs — its first step `/otel up` brings the collector up if
        // it isn't already, and its last step `/otel down` tears it back
        // down for full lifecycle reversibility.
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
            sb.AppendLine("DEMO RESULT: 0/14 PASS (14 live steps skipped — port conflict on :4318)");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        // ============================================================
        // LIVE DEMO STEPS. Every action step goes through another skill.
        // /otel up bookends the start; /otel down bookends the end —
        // the demo demonstrates the full lifecycle, leaving the collector
        // in a clean off state.
        // ============================================================
        sb.AppendLine("LIVE DEMO STEPS (each step invokes another skill — pure chain)");
        sb.AppendLine("==============================================================");

        var steps = new List<(int N, string Label, bool Pass, string Detail)>();

        // Step 1: bring the collector up (idempotent — no-op if already running).
        steps.Add(await OtelUp(skills, sessionId, 1));

        // Steps 2-4: persistent attributes via /otel set.
        steps.Add(await OtelSet(skills, sessionId, 2, "user", "Jaie"));
        steps.Add(await OtelSet(skills, sessionId, 3, "workstation", "LightningBlue"));
        steps.Add(await OtelSet(skills, sessionId, 4, "version", "0.001"));

        // Step 5: read back one persistent value via /otel get — proves the
        //         set+get round-trip and demonstrates a read-after-write skill chain.
        steps.Add(await OtelGet(skills, sessionId, 5, "user", expected: "Jaie"));

        // Step 6: per-session ticket via /enrich.
        steps.Add(await Enrich(skills, sessionId, 6, "ticket.id", "JA-0001"));

        // Steps 7-8: /weather working + /weather graceful failure.
        steps.Add(await Weather(skills, sessionId, 7, "London"));
        steps.Add(await Weather(skills, sessionId, 8, "$(rm -rf /)"));

        // Step 9: read JSONL — observation, not action. Counts records by ticket.
        steps.Add(JsonlSummary(9, "after JA-0001 set, before JA-0002"));

        // Step 10: change per-session ticket to JA-0002 via /enrich.
        steps.Add(await Enrich(skills, sessionId, 10, "ticket.id", "JA-0002"));

        // Steps 11-12: re-run /weather skills with JA-0002 active.
        steps.Add(await Weather(skills, sessionId, 11, "London"));
        steps.Add(await Weather(skills, sessionId, 12, "$(rm -rf /)"));

        // Step 13: read JSONL — observation. JA-0002 should now appear.
        steps.Add(JsonlSummary(13, "after JA-0002 set"));

        // Step 14: bring the collector back down — full lifecycle complete.
        steps.Add(await OtelDown(skills, sessionId, 14));

        var stepsPass = steps.Count(s => s.Pass);
        var stepsTotal = steps.Count;

        foreach (var s in steps)
        {
            var status = s.Pass ? "PASS" : "FAIL";
            sb.AppendLine($"STEP {s.N:00}: {status} — {s.Label}");
            if (!string.IsNullOrWhiteSpace(s.Detail))
                sb.AppendLine($"          {s.Detail}");
        }

        sb.AppendLine();
        sb.AppendLine($"DEMO RESULT: {stepsPass}/{stepsTotal} PASS");
        sb.AppendLine();
        AppendTeardownSection(sb);
        return Results.Text(sb.ToString(), "text/plain");
    }

    // ---------------------------------------------------------- skill-chain helpers

    private static async Task<(int, string, bool, string)> OtelUp(ISkillDispatchClient skills, string sessionId, int n)
    {
        var label = "/otel up (bring collector up; idempotent)";
        var r = await skills.DispatchAsync("otel", new Dictionary<string, string>
        {
            ["session_id"] = sessionId,
            ["args"]       = "up",
            ["skill_dir"]  = string.Empty,
        });
        // PASS if either "started" or "already running" (both are healthy outcomes
        // for an idempotent bring-up). FAIL otherwise (e.g. port conflict).
        var ok = r.IsSuccess && (r.Body.Contains("collector started") || r.Body.Contains("already running"));
        return (n, label, ok, $"chain → /skills/otel/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<(int, string, bool, string)> OtelDown(ISkillDispatchClient skills, string sessionId, int n)
    {
        var label = "/otel down (full lifecycle complete; system fully reversible)";
        var r = await skills.DispatchAsync("otel", new Dictionary<string, string>
        {
            ["session_id"] = sessionId,
            ["args"]       = "down",
            ["skill_dir"]  = string.Empty,
        });
        // PASS if "stopped", "already down", or "zombie cleaned" — all healthy
        // outcomes. FAIL on Conflict (BR-SECURITY-003 refuses to kill).
        var ok = r.IsSuccess && (r.Body.Contains("collector stopped") ||
                                 r.Body.Contains("already down") ||
                                 r.Body.Contains("zombie"));
        return (n, label, ok, $"chain → /skills/otel/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<(int, string, bool, string)> OtelSet(ISkillDispatchClient skills, string sessionId, int n, string k, string v)
    {
        var label = $"/otel set {k}:{v}";
        var args = $"set {k}:{v}";
        var r = await skills.DispatchAsync("otel", new Dictionary<string, string>
        {
            ["session_id"] = sessionId,
            ["args"]       = args,
        });
        return (n, label, r.IsSuccess, $"chain → /skills/otel/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<(int, string, bool, string)> OtelGet(ISkillDispatchClient skills, string sessionId, int n, string k, string expected)
    {
        var label = $"/otel get {k} (expect {expected} from earlier set)";
        var r = await skills.DispatchAsync("otel", new Dictionary<string, string>
        {
            ["session_id"] = sessionId,
            ["args"]       = $"get {k}",
        });
        var matches = r.IsSuccess && r.Body.Contains(expected);
        return (n, label, matches, $"chain → /skills/otel/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<(int, string, bool, string)> Enrich(ISkillDispatchClient skills, string sessionId, int n, string k, string v)
    {
        var label = $"/enrich {k} {v}";
        var r = await skills.DispatchAsync("enrich", new Dictionary<string, string>
        {
            ["session_id"] = sessionId,
            ["args"]       = $"{k} {v}",
        });
        return (n, label, r.IsSuccess, $"chain → /skills/enrich/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<(int, string, bool, string)> Weather(ISkillDispatchClient skills, string sessionId, int n, string location)
    {
        var label = $"/weather {location}";
        var r = await skills.DispatchAsync("weather", new Dictionary<string, string>
        {
            ["session_id"] = sessionId,
            ["args"]       = location,
        });
        // /weather is graceful by design — even injection-shaped input is
        // URL-escaped and either returns a weather string or upstream-failure
        // text. Any 2xx counts as PASS; non-2xx documents the failure.
        return (n, label, r.IsSuccess, $"chain → /skills/weather/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static (int, string, bool, string) JsonlSummary(int n, string when)
    {
        var label = $"observe {OutputFile} ({when})";
        if (!File.Exists(OutputFile))
            return (n, label, false, $"{OutputFile} does not exist yet — collector hasn't flushed");
        try
        {
            // The collector's fileexporter holds OutputFile open for append.
            // Open with FileShare.ReadWrite | FileShare.Delete so the read
            // coexists with the writer; without that the open fails with
            // "file in use by another process" on Windows. BR-DEMO-001 also
            // requires the handler to never throw — so any IO failure is
            // caught and converted to a FAIL row with the reason inline.
            var info = new FileInfo(OutputFile);
            using var stream = new FileStream(OutputFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var ja1 = lines.Count(l => l.Contains("JA-0001"));
            var ja2 = lines.Count(l => l.Contains("JA-0002"));
            return (n, label, true,
                $"{lines.Length} records, {info.Length} bytes; JA-0001 refs={ja1}, JA-0002 refs={ja2}");
        }
        catch (IOException ex)
        {
            return (n, label, false, $"could not read {OutputFile}: {ex.Message}");
        }
    }

    // ---------------------------------------------------------- helpers

    private static void AppendTeardownSection(StringBuilder sb)
    {
        sb.AppendLine("TEARDOWN (optional — for demo reversibility)");
        sb.AppendLine("============================================");
        sb.AppendLine("To stop the OTEL tenant (collector):");
        sb.AppendLine("  /otel down                 (lands with Plan-5 — until then, Ctrl-C the collector terminal)");
        sb.AppendLine();
        sb.AppendLine("To stop the deterministic-helpers platform (sidecar):");
        sb.AppendLine("  /skill-bootstrap stop");
        sb.AppendLine();
        sb.AppendLine("Re-run /demo any time to see the off-state again. The system is");
        sb.AppendLine("fully reversible — no machine state survives stop except the");
        sb.AppendLine($"{PersistentEnrichmentsFile} file, which can be cleared with");
        sb.AppendLine("`/otel config clear`.");
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

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length <= 80 ? s : s[..80] + "...";
    }
}
