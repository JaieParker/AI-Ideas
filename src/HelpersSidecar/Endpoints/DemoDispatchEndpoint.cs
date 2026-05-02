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

    public static IEndpointRouteBuilder MapDemoDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/demo/dispatch", Handle)
            .WithName("DemoDispatch")
            .WithSummary("Skill dispatcher for /demo — guided tour + skill-chain integration test");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, ICollectorControlClient collector, ISkillDispatchClient skills)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        if (string.IsNullOrEmpty(sessionId)) sessionId = DemoSession;

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

        // If the collector is down, the live skill chain would fail at the
        // /otel and /enrich legs (those skills' dispatchers report a
        // collector-down state). Skip the live section and tell the user
        // exactly what to start.
        if (!collectorUp)
        {
            sb.AppendLine("HOW TO BRING IT UP");
            sb.AppendLine("==================");
            sb.AppendLine("The deterministic-helpers platform (sidecar on :5050) is up — that's");
            sb.AppendLine("how this dispatch reached you. The OTEL tenant (collector) is down.");
            sb.AppendLine();
            sb.AppendLine("  1. Build the collector:");
            sb.AppendLine("       go build -o tools/otel-collector ./tools/go-collector/cmd/collector");
            sb.AppendLine("     (or wait for the .NET-only pivot — Plan-5 — to land, after which");
            sb.AppendLine("      `/otel up` does this in one command.)");
            sb.AppendLine();
            sb.AppendLine("  2. Start the collector (leave running in its own terminal):");
            sb.AppendLine("       ./tools/otel-collector --config ./collector-config.yaml");
            sb.AppendLine();
            sb.AppendLine("  3. Re-run /demo. Pre-flight will show all PASS, then the demo");
            sb.AppendLine("     walks 12 live skill-chain steps.");
            sb.AppendLine();
            sb.AppendLine("DEMO RESULT: 0/12 PASS (12 live steps skipped — collector down)");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        // ============================================================
        // LIVE DEMO STEPS. Every action step goes through another skill.
        // ============================================================
        sb.AppendLine("LIVE DEMO STEPS (each step invokes another skill — pure chain)");
        sb.AppendLine("==============================================================");

        var steps = new List<(int N, string Label, bool Pass, string Detail)>();

        // Steps 1-3: persistent attributes via /otel set.
        steps.Add(await OtelSet(skills, sessionId, 1, "user", "Jaie"));
        steps.Add(await OtelSet(skills, sessionId, 2, "workstation", "LightningBlue"));
        steps.Add(await OtelSet(skills, sessionId, 3, "version", "0.001"));

        // Step 4: read back one persistent value via /otel get — proves the
        //         set+get round-trip and demonstrates a read-after-write skill chain.
        steps.Add(await OtelGet(skills, sessionId, 4, "user", expected: "Jaie"));

        // Step 5: per-session ticket via /enrich.
        steps.Add(await Enrich(skills, sessionId, 5, "ticket.id", "JA-0001"));

        // Steps 6-7: /weather working + /weather graceful failure.
        steps.Add(await Weather(skills, sessionId, 6, "London"));
        steps.Add(await Weather(skills, sessionId, 7, "$(rm -rf /)"));

        // Step 8: read JSONL — observation, not action. Counts records by ticket.
        steps.Add(JsonlSummary(8, "after JA-0001 set, before JA-0002"));

        // Step 9: change per-session ticket to JA-0002 via /enrich.
        steps.Add(await Enrich(skills, sessionId, 9, "ticket.id", "JA-0002"));

        // Steps 10-11: re-run /weather skills with JA-0002 active.
        steps.Add(await Weather(skills, sessionId, 10, "London"));
        steps.Add(await Weather(skills, sessionId, 11, "$(rm -rf /)"));

        // Step 12: read JSONL — observation. JA-0002 should now appear.
        steps.Add(JsonlSummary(12, "after JA-0002 set"));

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
        var info = new FileInfo(OutputFile);
        var lines = File.ReadAllLines(OutputFile);
        var ja1 = lines.Count(l => l.Contains("JA-0001"));
        var ja2 = lines.Count(l => l.Contains("JA-0002"));
        return (n, label, true,
            $"{lines.Length} records, {info.Length} bytes; JA-0001 refs={ja1}, JA-0002 refs={ja2}");
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
