using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HelpersSidecar.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-DEMO-001 — /demo emits a structured pre-flight + live-step
/// table where every line is parseable as STEP NN(.x): PASS|FAIL
/// — &lt;detail&gt;, plus a final DEMO RESULT: x/y PASS line. The
/// pre-flight section runs honestly: when the collector is down,
/// the demo prints install instructions and skips the live steps.
/// When the collector is up, the demo walks 12 configure/observe
/// steps and emits a per-step PASS|FAIL.
/// </summary>
public class DemoDispatchEndpointTests
{
    [Fact(DisplayName = "BR-DEMO-001 — collector-down case: pre-flight FAILs, install instructions present, live steps skipped")]
    public async Task Demo_Collector_Down_Shows_Install_Instructions_And_Skips_Live_Steps()
    {
        var fake = new RecordingCollector { ReturnNullForAll = true };
        using var factory = FactoryWith(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        // Pre-flight section is present and emits the marker for the
        // collector row.
        Assert.Contains("PRE-FLIGHT", text);
        Assert.Contains("STEP 00.b: FAIL", text);

        // Install instructions explicitly name how to bring up the
        // collector tier.
        Assert.Contains("HOW TO BRING IT UP", text);
        Assert.Contains("./tools/otel-collector --config", text);

        // Live demo steps were skipped — final summary shows 0/12.
        Assert.Contains("DEMO RESULT: 0/12 PASS", text);

        // Teardown section is always present.
        Assert.Contains("TEARDOWN", text);
        Assert.Contains("/skill-bootstrap stop", text);
    }

    [Fact(DisplayName = "BR-DEMO-001 — collector-up case: pre-flight PASSes, 12 live steps emit PASS|FAIL markers, summary tallies match")]
    public async Task Demo_Collector_Up_Walks_Twelve_Live_Steps_With_Stable_Markers()
    {
        var fake = new RecordingCollector { ReturnNullForAll = false };
        using var factory = FactoryWith(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        // Pre-flight collector row is PASS now.
        Assert.Contains("STEP 00.b: PASS", text);

        // Live demo section is present.
        Assert.Contains("LIVE DEMO STEPS", text);

        // Every step from 01 through 12 emits a STEP NN: PASS|FAIL line.
        var stepRegex = new Regex(@"^STEP (\d{2}): (PASS|FAIL) — ", RegexOptions.Multiline);
        var matches = stepRegex.Matches(text)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(n => n >= 1 && n <= 12)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(Enumerable.Range(1, 12).ToArray(), matches);

        // Final summary line is present and parseable.
        var summary = Regex.Match(text, @"^DEMO RESULT: (\d+)/(\d+) PASS", RegexOptions.Multiline);
        Assert.True(summary.Success, "DEMO RESULT line missing");
        var pass = int.Parse(summary.Groups[1].Value);
        var total = int.Parse(summary.Groups[2].Value);
        Assert.Equal(12, total);
        Assert.True(pass >= 5,
            $"expected at least 5 of 12 live steps to PASS with a healthy fake collector, got {pass}");

        // Both ticket values went through the collector.
        Assert.Contains("JA-0001", fake.SessionValues);
        Assert.Contains("JA-0002", fake.SessionValues);
    }

    [Fact(DisplayName = "BR-DEMO-001 — every step ID is unique and within the documented range (00.a-00.d, 01-12)")]
    public async Task Demo_Step_Ids_Are_Unique_And_In_Range()
    {
        var fake = new RecordingCollector();
        using var factory = FactoryWith(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        var text = await response.Content.ReadAsStringAsync();

        var ids = Regex.Matches(text, @"^STEP (\S+): ", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToArray();

        // Every emitted ID either matches the pre-flight pattern (00.x) or
        // the live-step pattern (NN, two digits between 01 and 12).
        foreach (var id in ids)
        {
            var matchesPreflight = Regex.IsMatch(id, @"^00\.[a-z]$");
            var matchesLive = Regex.IsMatch(id, @"^\d{2}$") && int.Parse(id) is >= 1 and <= 12;
            Assert.True(matchesPreflight || matchesLive,
                $"step id {id} does not match preflight or live pattern");
        }

        // No duplicates.
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    private static FormUrlEncodedContent FormContent(params (string K, string V)[] kv)
    {
        var c = new FormUrlEncodedContent(kv.Select(p => new KeyValuePair<string, string>(p.K, p.V)));
        c.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return c;
    }

    private static WebApplicationFactory<Program> FactoryWith(ICollectorControlClient client)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ICollectorControlClient>();
                s.AddSingleton(client);
            }));

    private sealed class RecordingCollector : ICollectorControlClient
    {
        public bool ReturnNullForAll { get; set; }
        public List<string> SessionValues { get; } = new();

        private CollectorResponse? Ok(string body = "{}") =>
            ReturnNullForAll ? null : new CollectorResponse(200, body);

        public Task<bool> IsHealthyAsync(CancellationToken ct = default) =>
            Task.FromResult(!ReturnNullForAll);

        public Task<CollectorResponse?> SetPersistentAsync(string key, string value, CancellationToken ct = default)
            => Task.FromResult(Ok());

        public Task<CollectorResponse?> SetSessionEnrichmentAsync(string sessionId, string key, string value, CancellationToken ct = default)
        {
            SessionValues.Add(value);
            return Task.FromResult(Ok());
        }

        public Task<CollectorResponse?> GetSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> RemoveSessionEnrichmentAsync(string sessionId, string key, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> ClearSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> GetSessionCollectionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> SetSessionCollectionAsync(string sessionId, bool enabled, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> GetPersistentAsync(string? key, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> GetPersistentManyAsync(IEnumerable<string> keys, CancellationToken ct = default) => Task.FromResult(Ok("[]"));
        public Task<CollectorResponse?> RemovePersistentAsync(string key, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> ClearPersistentAsync(CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> RestartAsync(CancellationToken ct = default) => Task.FromResult(Ok());
    }
}
