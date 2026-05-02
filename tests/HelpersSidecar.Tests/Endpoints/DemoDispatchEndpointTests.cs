using System.Net;
using System.Net.Http.Headers;
using HelpersSidecar.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-PROCESS-001 — /skills/demo/dispatch orchestrates the full
/// 15-step demo, hitting the collector control client per step
/// and rendering a multi-line text response.
/// </summary>
public class DemoDispatchEndpointTests
{
    [Fact(DisplayName = "BR-ENRICH-007/008 — demo runs all 15 steps and calls the collector for set + per-session")]
    public async Task Demo_Runs_All_Steps()
    {
        var fake = new RecordingCollector();
        using var factory = FactoryWith(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        // 14+ step lines (15 steps; some get re-run; range allows for tweaks)
        var stepLines = text.Split('\n').Count(l => l.StartsWith("step "));
        Assert.InRange(stepLines, 13, 20);

        // Collector hit for the persistent sets
        Assert.True(fake.PersistentSetCalls >= 3,
            $"expected ≥3 persistent set calls (user/workstation/version), got {fake.PersistentSetCalls}");

        // Collector hit for the per-session sets (JA-0001 then JA-0002)
        Assert.True(fake.SessionSetCalls >= 2,
            $"expected ≥2 per-session set calls, got {fake.SessionSetCalls}");

        // Both ticket values were sent through
        Assert.Contains("JA-0001", fake.SessionValues);
        Assert.Contains("JA-0002", fake.SessionValues);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — demo reports collector-down gracefully when health fails")]
    public async Task Demo_Graceful_When_Collector_Down()
    {
        var fake = new RecordingCollector { ReturnNullForAll = true };
        using var factory = FactoryWith(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("collector not running", text);
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
        public int PersistentSetCalls { get; private set; }
        public int SessionSetCalls { get; private set; }
        public List<string> SessionValues { get; } = new();

        private CollectorResponse? Ok(string body = "{}") =>
            ReturnNullForAll ? null : new CollectorResponse(200, body);

        public Task<bool> IsHealthyAsync(CancellationToken ct = default) =>
            Task.FromResult(!ReturnNullForAll);

        public Task<CollectorResponse?> SetPersistentAsync(string key, string value, CancellationToken ct = default)
        {
            PersistentSetCalls++;
            return Task.FromResult(Ok());
        }

        public Task<CollectorResponse?> SetSessionEnrichmentAsync(string sessionId, string key, string value, CancellationToken ct = default)
        {
            SessionSetCalls++;
            SessionValues.Add(value);
            return Task.FromResult(Ok());
        }

        // unused below — return Ok/null per ReturnNullForAll
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
