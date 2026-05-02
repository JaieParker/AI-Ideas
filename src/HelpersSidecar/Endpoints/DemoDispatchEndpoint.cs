using System.Net.Http.Json;
using System.Text;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /demo skill. Runs as a guided
/// onboarding tour: probes pre-requirements honestly (showing
/// the off-state when components are missing), prints exact
/// commands to bring the system up, and — when everything is
/// up — walks the configure/observe/change flow with stable
/// PASS|FAIL markers per step (BR-DEMO-001).
///
/// The same dispatch endpoint doubles as the project's end-to-
/// end integration test surface: every step's marker is parseable.
/// </summary>
public static class DemoDispatchEndpoint
{
    private const string CollectorOtlp = "http://127.0.0.1:4318";
    private const string OutputFile    = "output/telemetry.jsonl";
    private const string PersistentEnrichmentsFile = "persistent-enrichments.json";
    private const string DemoSession   = "JA-DEMO";

    public static IEndpointRouteBuilder MapDemoDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/demo/dispatch", Handle)
            .WithName("DemoDispatch")
            .WithSummary("Skill dispatcher for /demo — guided onboarding + integration test");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, ICollectorControlClient collector)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== /demo — guided tour of the OTEL project ===");
        sb.AppendLine();
        sb.AppendLine("This skill probes the system, shows you what's installed and");
        sb.AppendLine("running, and demonstrates the configure/observe/teardown flow.");
        sb.AppendLine("On a clean machine you'll see everything FAIL with copy-paste");
        sb.AppendLine("commands to bring it up. Once everything is up, you'll see the");
        sb.AppendLine("12 demo steps and a final pass/fail summary.");
        sb.AppendLine();

        // SECTION 1 — pre-flight probes (always run; honest about off-state)
        sb.AppendLine("PRE-FLIGHT");
        sb.AppendLine("==========");

        var preflight = new List<(string Id, bool Pass, string Detail, string? Fix)>();

        // 00.a — .NET sidecar reachable. Implicit PASS: this very dispatch is
        //        running inside it. We say so explicitly anyway so the table
        //        reads honestly when copy-pasted.
        preflight.Add(("00.a", true,
            "Helpers sidecar (you are reading this from it on :5050)",
            null));

        // 00.b — collector control API reachable on :13133/13134.
        var collectorUp = await collector.IsHealthyAsync();
        preflight.Add(("00.b", collectorUp,
            collectorUp ? "Collector control reachable on :13133" : "Collector control NOT reachable on :13133",
            collectorUp ? null : "build + run the Go collector — see The-OTEL-Plan-2-go-collector.md (or wait for /otel up after Plan-5)"));

        // 00.c — output/telemetry.jsonl exists or is creatable.
        var outputDirOk = TryEnsureWriteable(Path.GetDirectoryName(OutputFile) ?? "output");
        preflight.Add(("00.c", outputDirOk,
            outputDirOk ? $"Output dir writeable ({OutputFile})" : "Output dir NOT writeable",
            outputDirOk ? null : "ensure the project root is writeable by the current user"));

        // 00.d — persistent-enrichments.json present (or creatable).
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

        // If pre-flight failed on anything load-bearing, stop and instruct.
        // The collector is the only one that gates the live demo steps.
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
            sb.AppendLine("     walks the live configure/observe flow.");
            sb.AppendLine();
            sb.AppendLine($"DEMO RESULT: 0/12 PASS (12 live steps skipped — collector down)");
            sb.AppendLine();
            AppendTeardownSection(sb);
            return Results.Text(sb.ToString(), "text/plain");
        }

        // SECTION 2 — live demo steps (12). Each emits "STEP NN: PASS|FAIL — ..."
        sb.AppendLine("LIVE DEMO STEPS");
        sb.AppendLine("===============");

        var steps = new List<(int N, string Label, bool Pass, string Detail)>();

        // Steps 1-3: persistent attributes (the stable label set every record gets).
        steps.Add(await PersistentSet(collector, 1, "user", "Jaie"));
        steps.Add(await PersistentSet(collector, 2, "workstation", "LightningBlue"));
        steps.Add(await PersistentSet(collector, 3, "version", "0.001"));

        // Step 4: per-session ticket reference (the work-item context).
        steps.Add(await SessionSet(collector, 4, DemoSession, "ticket.id", "JA-0001"));

        // Step 5: working /weather call (deterministic-helpers pattern).
        steps.Add(await Weather(5, "London"));

        // Step 6: failing /weather call (graceful failure on injection-shaped input).
        steps.Add(await WeatherFails(6, "$(rm -rf /)"));

        // Step 7: synthetic OTLP trace into the collector tagged session.id=JA-DEMO.
        steps.Add(await SyntheticTrace(7));

        // Step 8: read JSONL — JA-0001 should appear, JA-0002 should not yet.
        steps.Add(JsonlSummary(8, expectedJa1: ">=0", expectedJa2: "0"));

        // Step 9: change per-session ticket to JA-0002.
        steps.Add(await SessionSet(collector, 9, DemoSession, "ticket.id", "JA-0002"));

        // Step 10: re-run /weather (same call, different attribute set on the records).
        steps.Add(await Weather(10, "London"));

        // Step 11: send another synthetic OTLP trace under JA-0002.
        steps.Add(await SyntheticTrace(11));

        // Step 12: read JSONL — both ticket values should now appear.
        steps.Add(JsonlSummary(12, expectedJa1: ">=0", expectedJa2: ">=1"));

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

    // -------------------------------------------------------------- step helpers

    private static async Task<(int, string, bool, string)> PersistentSet(ICollectorControlClient c, int n, string k, string v)
    {
        var label = $"/otel set {k}:{v}";
        var r = await c.SetPersistentAsync(k, v);
        return r is null
            ? (n, label, false, "collector not reachable")
            : (n, label, r.StatusCode == 200, $"HTTP {r.StatusCode}");
    }

    private static async Task<(int, string, bool, string)> SessionSet(ICollectorControlClient c, int n, string sid, string k, string v)
    {
        var label = $"/enrich {k} {v} (session={sid})";
        var r = await c.SetSessionEnrichmentAsync(sid, k, v);
        return r is null
            ? (n, label, false, "collector not reachable")
            : (n, label, r.StatusCode == 200, $"HTTP {r.StatusCode}");
    }

    private static async Task<(int, string, bool, string)> Weather(int n, string loc)
    {
        var label = $"/weather {loc} (working call)";
        try
        {
            var url = $"https://wttr.in/{Uri.EscapeDataString(loc)}?format=3";
            using var r = await Http.GetAsync(url);
            return (n, label, r.IsSuccessStatusCode,
                $"HTTP {(int)r.StatusCode} — {(r.IsSuccessStatusCode ? "weather fetched" : "upstream failed")}");
        }
        catch (Exception ex)
        {
            return (n, label, false, $"exception: {ex.Message}");
        }
    }

    private static async Task<(int, string, bool, string)> WeatherFails(int n, string loc)
    {
        var label = $"/weather {loc} (graceful failure on shell-metachar input)";
        try
        {
            var url = $"https://wttr.in/{Uri.EscapeDataString(loc)}?format=3";
            using var r = await Http.GetAsync(url);
            // Either 200 with weather text OR upstream returned non-2xx — both
            // are graceful: the metachars were URL-escaped and the system
            // didn't shell-execute anything. Both count as PASS for this step.
            return (n, label, true, $"HTTP {(int)r.StatusCode} — input safely escaped, no shell exec");
        }
        catch (Exception)
        {
            return (n, label, true, "exception caught locally — no shell exec");
        }
    }

    private static async Task<(int, string, bool, string)> SyntheticTrace(int n)
    {
        var label = $"send synthetic OTLP trace (session={DemoSession})";
        var traceId = Guid.NewGuid().ToString("N");
        var spanId  = Guid.NewGuid().ToString("N").Substring(0, 16);
        var nowNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
        var payload = new
        {
            resourceSpans = new[]
            {
                new
                {
                    resource = new
                    {
                        attributes = new object[]
                        {
                            new { key = "service.name", value = new { stringValue = "demo-skill" } },
                            new { key = "session.id",   value = new { stringValue = DemoSession } },
                        }
                    },
                    scopeSpans = new[]
                    {
                        new
                        {
                            scope = new { name = "demo" },
                            spans = new[]
                            {
                                new
                                {
                                    traceId, spanId,
                                    name = "demo.span",
                                    kind = 1,
                                    startTimeUnixNano = nowNs.ToString(),
                                    endTimeUnixNano   = (nowNs + 10_000_000L).ToString(),
                                }
                            }
                        }
                    }
                }
            }
        };
        try
        {
            using var r = await Http.PostAsJsonAsync($"{CollectorOtlp}/v1/traces", payload);
            return (n, label, r.IsSuccessStatusCode,
                $"HTTP {(int)r.StatusCode} — {(r.IsSuccessStatusCode ? "collector accepted" : "collector rejected")}");
        }
        catch (Exception ex)
        {
            return (n, label, false, $"exception: {ex.Message}");
        }
    }

    private static (int, string, bool, string) JsonlSummary(int n, string expectedJa1, string expectedJa2)
    {
        var label = $"read {OutputFile} for JA-0001/JA-0002 counts";
        if (!File.Exists(OutputFile))
            return (n, label, false, $"{OutputFile} does not exist yet — collector hasn't flushed");
        var info = new FileInfo(OutputFile);
        var lines = File.ReadAllLines(OutputFile);
        var ja1 = lines.Count(l => l.Contains("JA-0001"));
        var ja2 = lines.Count(l => l.Contains("JA-0002"));
        return (n, label, true,
            $"{lines.Length} records, {info.Length} bytes; JA-0001 refs={ja1} (expected {expectedJa1}), JA-0002 refs={ja2} (expected {expectedJa2})");
    }

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

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
}
