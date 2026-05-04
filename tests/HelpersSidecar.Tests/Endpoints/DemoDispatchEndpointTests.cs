using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HelpersSidecar.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// /demo dispatch endpoint domain-scoped tests (Plan-23 rewrite).
///
/// Plan-23 retired the in-process loopback chain. The endpoint now
/// emits a structured DEMO_PLAN v1 + STEP_INVOKE markers; the
/// SKILL.md body executes each step via the Claude Code Skill tool
/// (BR-DEMO-002 amended). Tests scope to: pre-flight rows, plan-
/// marker shape, recovery behaviour, and the skip-on-port-conflict
/// branch. The downstream skills are out of scope here — they have
/// their own domain tests.
/// </summary>
public class DemoDispatchEndpointTests
{
    [Fact(DisplayName = "BR-OTEL-005 — port :4318 held by another process: 00.e FAILs, plan section skipped, both recovery options shown")]
    public async Task Demo_OtlpPort_Conflict_Reports_Conflict_With_Both_Recovery_Options()
    {
        var collector = new RecordingCollector { ReturnNullForAll = true };
        var ports = new FakePortProbe { Listening = { 4318 } };
        using var factory = FactoryWith(collector, ports);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("STEP 00.e: FAIL", text);
        Assert.Contains("CONFLICT", text);
        Assert.Contains("PORT CONFLICT", text);
        Assert.Contains("Option A — stop the holder", text);
        Assert.Contains("Get-NetTCPConnection -LocalPort 4318", text);
        Assert.Contains("Option B — re-port the project collector", text);
        Assert.Contains("config.yaml", text);
        Assert.Contains("BR-SECURITY-003", text);
        Assert.Contains("DEMO RESULT: SKIPPED", text);
        Assert.DoesNotContain("DEMO_PLAN v1", text);
    }

    [Fact(DisplayName = "BR-DEMO-001 — collector down but :4318 free: dispatch emits DEMO_PLAN v1 with the OTEL default 14-step plan + RECOVERY_AVAILABLE marker")]
    public async Task Demo_Collector_Down_With_Free_Port_Emits_Plan()
    {
        var collector = new RecordingCollector { ReturnNullForAll = true };
        var ports = new FakePortProbe();
        using var factory = FactoryWith(collector, ports);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("DEMO_PLAN v1: target=\"otel\" target_kind=\"domain\" demo=\"happy-path\" steps=14", text);
        Assert.Contains("RECOVERY_AVAILABLE v1: skill=\"otel\" verb=\"up\"", text);

        var invokeMatches = Regex.Matches(text, @"^STEP_INVOKE: number=(\d+) skill=""([^""]+)"" args=""([^""]*)""", RegexOptions.Multiline);
        Assert.Equal(12, invokeMatches.Count);
        Assert.Equal("otel", invokeMatches.First().Groups[2].Value);
        Assert.Equal("up", invokeMatches.First().Groups[3].Value);
        Assert.Equal("otel", invokeMatches.Last().Groups[2].Value);
        Assert.Equal("down", invokeMatches.Last().Groups[3].Value);
    }

    [Fact(DisplayName = "BR-OTEL-005 — port :4318 owned by our collector: 00.e PASSes")]
    public async Task Demo_OtlpPort_Owned_By_Our_Collector_Passes()
    {
        var collector = new RecordingCollector { ReturnNullForAll = false };
        var ports = new FakePortProbe { Listening = { 4318 } };
        using var factory = FactoryWith(collector, ports);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("STEP 00.e: PASS", text);
        Assert.Contains("owned by project collector", text);
        Assert.DoesNotContain("CONFLICT", text);
    }

    [Fact(DisplayName = "BR-DEMO-001 — collector-up case: 14 STEP markers (12 invoke + 2 observe) + run_id + finalize URL emitted")]
    public async Task Demo_Collector_Up_Plan_Has_Fourteen_Step_Markers_With_RunId()
    {
        var collector = new RecordingCollector { ReturnNullForAll = false };
        var ports = new FakePortProbe();
        using var factory = FactoryWith(collector, ports);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("STEP 00.b: PASS", text);

        var invokes = Regex.Matches(text, @"^STEP_INVOKE: number=(\d+)", RegexOptions.Multiline);
        var observes = Regex.Matches(text, @"^STEP_OBSERVE: number=(\d+)", RegexOptions.Multiline);
        Assert.Equal(12, invokes.Count);
        Assert.Equal(2, observes.Count);

        var allNumbers = invokes.Select(m => int.Parse(m.Groups[1].Value))
            .Concat(observes.Select(m => int.Parse(m.Groups[1].Value)))
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(Enumerable.Range(1, 14).ToArray(), allNumbers);

        var runIdMatch = Regex.Match(text, @"run_id=""([0-9a-f]{32})""");
        Assert.True(runIdMatch.Success, "DEMO_PLAN v1 line missing run_id");

        var finalize = Regex.Match(text, @"--data-urlencode 'run_id=([0-9a-f]{32})' --data-urlencode 'finalize=true'");
        Assert.True(finalize.Success, "post-plan finalize curl line missing");
        Assert.Equal(runIdMatch.Groups[1].Value, finalize.Groups[1].Value);
    }

    [Fact(DisplayName = "BR-DEMO-001 — every pre-flight step ID is unique and within 00.a-00.e")]
    public async Task Demo_Preflight_Ids_Are_Unique_And_In_Range()
    {
        var collector = new RecordingCollector();
        var ports = new FakePortProbe();
        using var factory = FactoryWith(collector, ports);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        var text = await response.Content.ReadAsStringAsync();

        var ids = Regex.Matches(text, @"^STEP (00\.[a-z]): ", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact(DisplayName = "BR-DEMO-002 (amended) — dispatch never invokes downstream skills via in-process loopback (returns plan only)")]
    public async Task Demo_Dispatch_Never_Chains_In_Process()
    {
        var collector = new RecordingCollector { ReturnNullForAll = false };
        var ports = new FakePortProbe();
        using var factory = FactoryWith(collector, ports);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The healthz probe is the only collector contact /demo is
        // allowed to make — that's a status check, not an action.
        Assert.True(collector.HealthChecks >= 1, "expected at least one IsHealthyAsync probe for pre-flight 00.b");

        // No collector-control action methods may be invoked.
        Assert.Equal(0, collector.SetPersistentCalls);
        Assert.Equal(0, collector.SetSessionCalls);
        Assert.Equal(0, collector.GetPersistentCalls);
        Assert.Equal(0, collector.RestartCalls);
    }

    [Fact(DisplayName = "BR-DEMO-002 (amended) — unknown demo case for known target emits DEMO_UNKNOWN v1")]
    public async Task Demo_Unknown_Case_Emits_Unknown_Marker()
    {
        var collector = new RecordingCollector { ReturnNullForAll = false };
        var ports = new FakePortProbe();
        using var factory = FactoryWith(collector, ports);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session"), ("args", "otel does-not-exist")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("DEMO_UNKNOWN v1: target=\"otel\" demo=\"does-not-exist\"", text);
        Assert.Contains("DEMO RESULT: SKIPPED (no demo case)", text);
    }

    // ---------------------------------------------------------------- harness

    private static FormUrlEncodedContent FormContent(params (string K, string V)[] kv)
    {
        var c = new FormUrlEncodedContent(kv.Select(p => new KeyValuePair<string, string>(p.K, p.V)));
        c.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return c;
    }

    private static WebApplicationFactory<Program> FactoryWith(
        ICollectorControlClient collector,
        IPortProbe ports)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Otel:CollectorOtlpPort"] = "4318",
                });
            });
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ICollectorControlClient>();
                s.AddSingleton(collector);
                s.RemoveAll<IPortProbe>();
                s.AddSingleton(ports);
            });
        });

    private sealed class FakePortProbe : IPortProbe
    {
        public HashSet<int> Listening { get; } = new();
        public bool IsListening(int port) => Listening.Contains(port);
    }

    private sealed class RecordingCollector : ICollectorControlClient
    {
        public bool ReturnNullForAll { get; set; }
        public int HealthChecks { get; private set; }
        public int SetPersistentCalls { get; private set; }
        public int SetSessionCalls { get; private set; }
        public int GetPersistentCalls { get; private set; }
        public int RestartCalls { get; private set; }

        private CollectorResponse? Ok(string body = "{}") =>
            ReturnNullForAll ? null : new CollectorResponse(200, body);

        public Task<bool> IsHealthyAsync(CancellationToken ct = default)
        {
            HealthChecks++;
            return Task.FromResult(!ReturnNullForAll);
        }

        public Task<CollectorResponse?> SetPersistentAsync(string key, string value, CancellationToken ct = default)
        { SetPersistentCalls++; return Task.FromResult(Ok()); }

        public Task<CollectorResponse?> SetSessionEnrichmentAsync(string sessionId, string key, string value, CancellationToken ct = default)
        { SetSessionCalls++; return Task.FromResult(Ok()); }

        public Task<CollectorResponse?> GetPersistentAsync(string? key, CancellationToken ct = default)
        { GetPersistentCalls++; return Task.FromResult(Ok()); }

        public Task<CollectorResponse?> RestartAsync(CancellationToken ct = default)
        { RestartCalls++; return Task.FromResult(Ok()); }

        public Task<CollectorResponse?> GetSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> RemoveSessionEnrichmentAsync(string sessionId, string key, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> ClearSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> GetSessionCollectionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> SetSessionCollectionAsync(string sessionId, bool enabled, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> GetPersistentManyAsync(IEnumerable<string> keys, CancellationToken ct = default) => Task.FromResult(Ok("[]"));
        public Task<CollectorResponse?> RemovePersistentAsync(string key, CancellationToken ct = default) => Task.FromResult(Ok());
        public Task<CollectorResponse?> ClearPersistentAsync(CancellationToken ct = default) => Task.FromResult(Ok());
    }
}
