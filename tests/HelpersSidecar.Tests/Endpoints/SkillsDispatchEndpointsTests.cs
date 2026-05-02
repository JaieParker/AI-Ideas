using System.Net.Http.Headers;
using System.Net.Http.Json;
using HelpersSidecar.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-SKILL-007 — skill dispatch lives on the sidecar, not in
/// per-skill helper scripts. These tests drive the dispatch
/// endpoints over HTTP with a fake collector client to keep the
/// suite hermetic.
/// </summary>
public class SkillsDispatchEndpointsTests
{
    [Fact(DisplayName = "BR-SKILL-007 — /skills/enrich/dispatch shows usage on empty args")]
    public async Task Enrich_Dispatch_Shows_Usage_On_Empty()
    {
        using var factory = FactoryWith(new FakeCollector());
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/enrich/dispatch",
            FormContent(("session_id", "s1"), ("args", "")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("usage:", text);
    }

    [Fact(DisplayName = "BR-SKILL-007 — /skills/enrich/dispatch routes 'set' through the collector client")]
    public async Task Enrich_Dispatch_Set_Calls_Collector()
    {
        var fake = new FakeCollector();
        using var factory = FactoryWith(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/enrich/dispatch",
            FormContent(("session_id", "s1"), ("args", "ticket.id PROJ-1234")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("set ticket.id=PROJ-1234", text);
        Assert.Equal(1, fake.SetSessionCalls);
        Assert.Equal("ticket.id", fake.LastKey);
        Assert.Equal("PROJ-1234", fake.LastValue);
    }

    [Fact(DisplayName = "BR-SKILL-007 — /skills/enrich/dispatch reports collector down gracefully")]
    public async Task Enrich_Dispatch_Reports_Collector_Down()
    {
        using var factory = FactoryWith(new FakeCollector { ReturnNullForAll = true });
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/enrich/dispatch",
            FormContent(("session_id", "s1"), ("args", "--show")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("not reachable", text);
        Assert.Contains("/otel", text);
    }

    [Fact(DisplayName = "BR-SKILL-007 — /skills/otel/dispatch with empty args runs Setup and prints HELP")]
    public async Task Otel_Dispatch_Setup_Prints_Help()
    {
        var skillDir = Path.Combine(Path.GetTempPath(), "otel-skill-test-" + Guid.NewGuid());
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "HELP.md"), "## fake help text");

        try
        {
            using var factory = FactoryWith(new FakeCollector());
            var client = factory.CreateClient();

            var response = await client.PostAsync("/skills/otel/dispatch",
                FormContent(("session_id", "s1"), ("args", ""), ("skill_dir", skillDir)));
            var text = await response.Content.ReadAsStringAsync();

            Assert.Contains("Helpers sidecar", text);
            Assert.Contains("fake help text", text);
        }
        finally { Directory.Delete(skillDir, recursive: true); }
    }

    [Fact(DisplayName = "BR-SKILL-007 — /skills/otel/dispatch 'extend topic' emits the chain marker")]
    public async Task Otel_Dispatch_Extend_Emits_Marker()
    {
        using var factory = FactoryWith(new FakeCollector());
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "extend fix the foo"), ("skill_dir", "")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("EXTEND_REQUESTED:", text);
        Assert.Contains("fix the foo", text);
        Assert.Contains("otel-extend", text);
    }

    [Fact(DisplayName = "BR-ENRICH-013 — /skills/otel/dispatch 'get team' single-key proxies through collector")]
    public async Task Otel_Dispatch_Get_Single_Key()
    {
        var fake = new FakeCollector { PersistentSingleResponse = new CollectorResponse(200, "platform") };
        using var factory = FactoryWith(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "get team"), ("skill_dir", "")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal("team=platform", text.TrimEnd());
        Assert.Equal("team", fake.LastPersistentGetKey);
    }

    [Fact(DisplayName = "BR-ENRICH-013 — /skills/otel/dispatch 'get unset' returns (unset)")]
    public async Task Otel_Dispatch_Get_Unset()
    {
        var fake = new FakeCollector { PersistentSingleResponse = new CollectorResponse(404, "") };
        using var factory = FactoryWith(fake);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "get missing"), ("skill_dir", "")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal("missing=(unset)", text.TrimEnd());
    }

    // ---------------- helpers ----------------

    private static FormUrlEncodedContent FormContent(params (string K, string V)[] kv)
    {
        var content = new FormUrlEncodedContent(kv.Select(p => new KeyValuePair<string, string>(p.K, p.V)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return content;
    }

    private static WebApplicationFactory<Program> FactoryWith(ICollectorControlClient client)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICollectorControlClient>();
                services.AddSingleton(client);
            }));

    /// <summary>
    /// Hand-rolled fake — small enough that a full mocking framework
    /// would be more code than the fake itself.
    /// </summary>
    private sealed class FakeCollector : ICollectorControlClient
    {
        public bool ReturnNullForAll { get; set; }
        public CollectorResponse? PersistentSingleResponse { get; set; }
        public int SetSessionCalls { get; private set; }
        public string? LastKey { get; private set; }
        public string? LastValue { get; private set; }
        public string? LastPersistentGetKey { get; private set; }

        private CollectorResponse? Ok(string body = "{}") => ReturnNullForAll ? null : new CollectorResponse(200, body);

        public Task<CollectorResponse?> GetSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult(Ok());

        public Task<CollectorResponse?> SetSessionEnrichmentAsync(string sessionId, string key, string value, CancellationToken ct = default)
        {
            SetSessionCalls++;
            LastKey = key;
            LastValue = value;
            return Task.FromResult(Ok());
        }

        public Task<CollectorResponse?> RemoveSessionEnrichmentAsync(string sessionId, string key, CancellationToken ct = default)
            => Task.FromResult(Ok());
        public Task<CollectorResponse?> ClearSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult(Ok());
        public Task<CollectorResponse?> GetSessionCollectionAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult(Ok("\"on\""));
        public Task<CollectorResponse?> SetSessionCollectionAsync(string sessionId, bool enabled, CancellationToken ct = default)
            => Task.FromResult(Ok());

        public Task<CollectorResponse?> GetPersistentAsync(string? key, CancellationToken ct = default)
        {
            LastPersistentGetKey = key;
            if (ReturnNullForAll) return Task.FromResult<CollectorResponse?>(null);
            return Task.FromResult<CollectorResponse?>(PersistentSingleResponse ?? new CollectorResponse(200, "{}"));
        }

        public Task<CollectorResponse?> GetPersistentManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
            => Task.FromResult(Ok("[]"));
        public Task<CollectorResponse?> SetPersistentAsync(string key, string value, CancellationToken ct = default)
            => Task.FromResult(Ok());
        public Task<CollectorResponse?> RemovePersistentAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Ok());
        public Task<CollectorResponse?> ClearPersistentAsync(CancellationToken ct = default)
            => Task.FromResult(Ok());
        public Task<CollectorResponse?> RestartAsync(CancellationToken ct = default)
            => Task.FromResult(Ok());
        public Task<bool> IsHealthyAsync(CancellationToken ct = default)
            => Task.FromResult(!ReturnNullForAll);
    }
}
