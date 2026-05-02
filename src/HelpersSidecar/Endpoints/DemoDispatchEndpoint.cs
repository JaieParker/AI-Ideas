using System.Net.Http.Json;
using System.Text;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /demo skill. Runs the canonical
/// 15-step enrichment demo against the running collector and
/// returns formatted multi-line text.
/// </summary>
public static class DemoDispatchEndpoint
{
    private const string CollectorOtlp = "http://127.0.0.1:4318";
    private const string OutputFile    = "output/telemetry.jsonl";
    private const string DemoSession   = "JA-DEMO";

    public static IEndpointRouteBuilder MapDemoDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/demo/dispatch", Handle)
            .WithName("DemoDispatch")
            .WithSummary("Skill dispatcher for /demo — runs the 15-step end-to-end demo");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, ICollectorControlClient collector)
    {
        var sb = new StringBuilder();
        var step = 0;

        async Task Run(string label, Func<Task<string>> action)
        {
            step++;
            try
            {
                var result = await action();
                sb.AppendLine($"step {step,2}: {label}");
                if (!string.IsNullOrWhiteSpace(result))
                    sb.AppendLine($"          → {result}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"step {step,2}: {label}");
                sb.AppendLine($"          ! {ex.Message}");
            }
        }

        // Pre-flight: confirm collector is up. If not, every subsequent step
        // gracefully reports — the output reads honestly and points at /otel.
        var collectorUp = await collector.IsHealthyAsync();

        // 1. /weather working
        await Run("/weather London (working)",
            () => CallWeather("London"));
        // 2. /weather failing (injection-shaped input)
        await Run("/weather $(rm -rf /) (graceful failure)",
            () => CallWeather("$(rm -rf /)"));
        // 3. show OTEL logs (initial)
        await Run("show OTEL logs (initial state)",
            () => Task.FromResult(SummariseLogs()));

        // 4-6. set persistent: user, workstation, version
        foreach (var (k, v) in new[] { ("user", "Jaie"), ("workstation", "LightningBlue"), ("version", "0.001") })
        {
            await Run($"/otel set {k}:{v}", async () =>
            {
                if (!collectorUp) return "collector not running — /otel to start";
                var r = await collector.SetPersistentAsync(k, v);
                return r is null ? "collector not reachable" : $"HTTP {r.StatusCode}";
            });
        }

        // 7. set per-session ticket-reference: JA-0001
        await Run($"/enrich ticket-reference JA-0001 (session {DemoSession})", async () =>
        {
            if (!collectorUp) return "collector not running — /otel to start";
            var r = await collector.SetSessionEnrichmentAsync(DemoSession, "ticket.id", "JA-0001");
            return r is null ? "collector not reachable" : $"HTTP {r.StatusCode}";
        });

        // 8. re-run 1, 2, 3 (after persistent + JA-0001)
        await Run("re-run /weather London (after JA-0001 set)", () => CallWeather("London"));
        await Run("re-run /weather injection (after JA-0001 set)", () => CallWeather("$(rm -rf /)"));
        await Run("send synthetic OTLP trace tagged session.id=JA-DEMO",
            () => SendSyntheticTrace());

        // 9. change ticket-reference to JA-0002
        await Run($"/enrich ticket-reference JA-0002 (session {DemoSession})", async () =>
        {
            if (!collectorUp) return "collector not running";
            var r = await collector.SetSessionEnrichmentAsync(DemoSession, "ticket.id", "JA-0002");
            return r is null ? "collector not reachable" : $"HTTP {r.StatusCode}";
        });

        // 10. re-run 1, 2, 3 (after JA-0002)
        await Run("re-run /weather London (after JA-0002 set)", () => CallWeather("London"));
        await Run("re-run /weather injection (after JA-0002 set)", () => CallWeather("$(rm -rf /)"));
        await Run("send synthetic OTLP trace tagged session.id=JA-DEMO (with JA-0002 active)",
            () => SendSyntheticTrace());

        // 11. show final logs
        await Run("show final OTEL logs (JA-0001 → JA-0002 transition observable)",
            () => Task.FromResult(SummariseLogs()));

        sb.AppendLine();
        sb.AppendLine("Inspect output/telemetry.jsonl directly to filter records by ticket.id");
        sb.AppendLine("(`grep JA-0001 output/telemetry.jsonl` vs `grep JA-0002 output/telemetry.jsonl`).");

        return Results.Text(sb.ToString(), "text/plain");
    }

    // -------------------------------------------------------

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static async Task<string> CallWeather(string location)
    {
        try
        {
            var url = $"https://wttr.in/{Uri.EscapeDataString(location)}?format=3";
            using var r = await Http.GetAsync(url);
            if (!r.IsSuccessStatusCode) return $"HTTP {(int)r.StatusCode}";
            return (await r.Content.ReadAsStringAsync()).Trim();
        }
        catch (Exception ex) { return $"failed: {ex.Message}"; }
    }

    private static string SummariseLogs()
    {
        if (!File.Exists(OutputFile)) return "(output/telemetry.jsonl does not exist yet)";
        var info = new FileInfo(OutputFile);
        var lines = File.ReadAllLines(OutputFile);
        var ja1 = lines.Count(l => l.Contains("JA-0001"));
        var ja2 = lines.Count(l => l.Contains("JA-0002"));
        return $"{lines.Length} OTLP records, {info.Length} bytes; JA-0001 refs={ja1}, JA-0002 refs={ja2}";
    }

    private static async Task<string> SendSyntheticTrace()
    {
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
                                    traceId,
                                    spanId,
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
            return r.IsSuccessStatusCode ? "200 (collector accepted)" : $"HTTP {(int)r.StatusCode}";
        }
        catch (Exception ex) { return $"failed: {ex.Message}"; }
    }
}
