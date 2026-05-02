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
/// /demo dispatch endpoint domain-scoped tests.
///
/// Per BR-PROCESS-007 (minimum-domain test scope), these tests
/// scope strictly to /demo's orchestration domain. The downstream
/// skills (/otel, /enrich, /weather) are mocked via
/// <see cref="RecordingSkillDispatchClient"/>; they have their
/// own domain tests. A change to /weather's parsing or /otel's
/// verb table does NOT re-run these /demo tests.
///
/// BR-DEMO-001 — pre-flight, live, and teardown sections are
///                emitted with stable PASS|FAIL markers.
/// BR-DEMO-002 — /demo invokes other skills via
///                ISkillDispatchClient and never calls the
///                collector control client for actions (status
///                probes only).
/// </summary>
public class DemoDispatchEndpointTests
{
    [Fact(DisplayName = "BR-DEMO-001 — collector-down case: pre-flight FAILs, install instructions present, live steps skipped, no skill chains attempted")]
    public async Task Demo_Collector_Down_Shows_Install_Instructions_And_Skips_Live_Steps()
    {
        var collector = new RecordingCollector { ReturnNullForAll = true };
        var skills = new RecordingSkillDispatchClient();
        using var factory = FactoryWith(collector, skills);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("PRE-FLIGHT", text);
        Assert.Contains("STEP 00.b: FAIL", text);
        Assert.Contains("HOW TO BRING IT UP", text);
        Assert.Contains("./tools/otel-collector --config", text);
        Assert.Contains("DEMO RESULT: 0/12 PASS", text);
        Assert.Contains("TEARDOWN", text);
        Assert.Contains("/skill-bootstrap stop", text);

        // BR-DEMO-002 — collector-down case skips live steps; no skill
        // chains should have been attempted.
        Assert.Empty(skills.Calls);
    }

    [Fact(DisplayName = "BR-DEMO-001 — collector-up case: 12 live STEP markers + parseable summary line")]
    public async Task Demo_Collector_Up_Walks_Twelve_Live_Steps_With_Stable_Markers()
    {
        var collector = new RecordingCollector { ReturnNullForAll = false };
        var skills = new RecordingSkillDispatchClient();
        using var factory = FactoryWith(collector, skills);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("STEP 00.b: PASS", text);
        Assert.Contains("LIVE DEMO STEPS", text);

        var stepRegex = new Regex(@"^STEP (\d{2}): (PASS|FAIL) — ", RegexOptions.Multiline);
        var liveSteps = stepRegex.Matches(text)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(n => n >= 1 && n <= 12)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(Enumerable.Range(1, 12).ToArray(), liveSteps);

        var summary = Regex.Match(text, @"^DEMO RESULT: (\d+)/(\d+) PASS", RegexOptions.Multiline);
        Assert.True(summary.Success, "DEMO RESULT line missing");
        Assert.Equal(12, int.Parse(summary.Groups[2].Value));
    }

    [Fact(DisplayName = "BR-DEMO-001 — every step ID is unique and within the documented range (00.a-00.d, 01-12)")]
    public async Task Demo_Step_Ids_Are_Unique_And_In_Range()
    {
        var collector = new RecordingCollector();
        var skills = new RecordingSkillDispatchClient();
        using var factory = FactoryWith(collector, skills);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        var text = await response.Content.ReadAsStringAsync();

        var ids = Regex.Matches(text, @"^STEP (\S+): ", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToArray();

        foreach (var id in ids)
        {
            var matchesPreflight = Regex.IsMatch(id, @"^00\.[a-z]$");
            var matchesLive = Regex.IsMatch(id, @"^\d{2}$") && int.Parse(id) is >= 1 and <= 12;
            Assert.True(matchesPreflight || matchesLive,
                $"step id {id} does not match preflight or live pattern");
        }

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact(DisplayName = "BR-DEMO-002 — collector-up case: every action step invokes another skill via ISkillDispatchClient (skill-chain orchestrator)")]
    public async Task Demo_Action_Steps_Go_Through_Skill_Dispatch_Client()
    {
        var collector = new RecordingCollector { ReturnNullForAll = false };
        var skills = new RecordingSkillDispatchClient();
        using var factory = FactoryWith(collector, skills);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Expected chain (10 skill calls): 3 otel-set + 1 otel-get + 2 enrich + 4 weather.
        // Steps 8 and 12 are JSONL reads (observation, not action) and do NOT
        // count as skill calls.
        var bySkill = skills.Calls.GroupBy(c => c.SkillName)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(4, bySkill["otel"]);    // set user, set workstation, set version, get user
        Assert.Equal(2, bySkill["enrich"]);  // ticket.id JA-0001, ticket.id JA-0002
        Assert.Equal(4, bySkill["weather"]); // 2 working + 2 graceful-failure runs
        Assert.Equal(10, skills.Calls.Count);

        // The args sent through the chain capture the user-facing slash args
        // verbatim — proves /demo is going through the same parsing layer
        // a real user would hit.
        Assert.Contains(skills.Calls, c =>
            c.SkillName == "otel" && c.Args == "set user:Jaie");
        Assert.Contains(skills.Calls, c =>
            c.SkillName == "enrich" && c.Args == "ticket.id JA-0001");
        Assert.Contains(skills.Calls, c =>
            c.SkillName == "enrich" && c.Args == "ticket.id JA-0002");
        Assert.Contains(skills.Calls, c =>
            c.SkillName == "weather" && c.Args == "London");
    }

    [Fact(DisplayName = "BR-DEMO-002 — /demo never calls the collector control client for actions (status probes only)")]
    public async Task Demo_Never_Touches_Collector_Control_For_Actions()
    {
        var collector = new RecordingCollector { ReturnNullForAll = false };
        var skills = new RecordingSkillDispatchClient();
        using var factory = FactoryWith(collector, skills);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/demo/dispatch",
            FormContent(("session_id", "test-session")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The healthz probe is the only collector contact /demo is allowed
        // to make — that's a status check, not an action.
        Assert.True(collector.HealthChecks >= 1, "expected at least one IsHealthyAsync probe for pre-flight 00.b");

        // No collector-control action methods may be invoked.
        Assert.Equal(0, collector.SetPersistentCalls);
        Assert.Equal(0, collector.SetSessionCalls);
        Assert.Equal(0, collector.GetPersistentCalls);
        Assert.Equal(0, collector.RestartCalls);
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
        ISkillDispatchClient skills)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ICollectorControlClient>();
                s.AddSingleton(collector);
                s.RemoveAll<ISkillDispatchClient>();
                s.AddSingleton(skills);
            }));

    /// <summary>
    /// Records calls and returns canned responses. Pre-seeded with a
    /// /otel get response that contains "Jaie" so the round-trip
    /// assertion (step 4) passes.
    /// </summary>
    private sealed class RecordingSkillDispatchClient : ISkillDispatchClient
    {
        public List<(string SkillName, string Args, string SessionId)> Calls { get; } = new();

        public Task<SkillDispatchResult> DispatchAsync(string skillName, IReadOnlyDictionary<string, string> form, CancellationToken ct = default)
        {
            var args = form.TryGetValue("args", out var a) ? a : string.Empty;
            var sid = form.TryGetValue("session_id", out var s) ? s : string.Empty;
            Calls.Add((skillName, args, sid));

            // Canned bodies sufficient for /demo's assertions:
            //   /otel get user → must contain "Jaie" so step 4's expect-match
            //                    passes.
            //   everything else → "ok" (200).
            var body = (skillName, args) switch
            {
                ("otel", "get user") => "user=Jaie",
                _                     => "ok",
            };
            return Task.FromResult(new SkillDispatchResult(200, body));
        }
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
