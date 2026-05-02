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
/// BR-OTEL-006 — /otel up and /otel down own the collector tier
/// lifecycle. Tests scope to the dispatch endpoint's logic by
/// mocking <see cref="IProcessLifecycle"/> at the seam (per
/// BR-PROCESS-007).
/// </summary>
public class OtelUpDownDispatchTests
{
    [Fact(DisplayName = "BR-OTEL-006 — /otel up spawns collector when state is NotRunning")]
    public async Task Up_Spawns_When_NotRunning()
    {
        var lifecycle = new FakeLifecycle
        {
            ProbeResult = new LifecycleStatus(LifecycleState.NotRunning, null, 13133, "no PID file, port free"),
            SpawnResult = new SpawnResult(Spawned: true, Pid: 4242, Reason: "spawned collector as PID 4242"),
        };
        using var factory = FactoryWith(lifecycle);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "up"), ("skill_dir", "")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("collector started", text);
        Assert.Contains("PID 4242", text);
        Assert.Equal(1, lifecycle.SpawnCalls);
        Assert.Equal("collector", lifecycle.LastSpawnComponent);
    }

    [Fact(DisplayName = "BR-OTEL-006 — /otel up is a no-op when state is RunningOurs")]
    public async Task Up_NoOp_When_RunningOurs()
    {
        var lifecycle = new FakeLifecycle
        {
            ProbeResult = new LifecycleStatus(LifecycleState.RunningOurs, 4242, 13133, "alive and bound"),
        };
        using var factory = FactoryWith(lifecycle);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "up"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("already running", text);
        Assert.Contains("4242", text);
        Assert.Equal(0, lifecycle.SpawnCalls);  // no double-spawn
    }

    [Fact(DisplayName = "BR-OTEL-006 — /otel up refuses to spawn when state is Conflict (BR-OTEL-005 recovery options)")]
    public async Task Up_Refuses_On_Conflict()
    {
        var lifecycle = new FakeLifecycle
        {
            ProbeResult = new LifecycleStatus(LifecycleState.Conflict, null, 13133, "port held by another process"),
        };
        using var factory = FactoryWith(lifecycle);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "up"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("collector cannot start", text);
        Assert.Contains("BR-OTEL-005", text);
        Assert.Equal(0, lifecycle.SpawnCalls);  // never spawn over a conflict
    }

    [Fact(DisplayName = "BR-OTEL-006 — /otel down stops collector when state is RunningOurs")]
    public async Task Down_Stops_When_RunningOurs()
    {
        var lifecycle = new FakeLifecycle
        {
            ProbeResult = new LifecycleStatus(LifecycleState.RunningOurs, 4242, 13133, "alive"),
            StopResult = true,
        };
        using var factory = FactoryWith(lifecycle);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "down"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("collector stopped", text);
        Assert.Equal(1, lifecycle.StopCalls);
        Assert.Equal("collector", lifecycle.LastStopComponent);
    }

    [Fact(DisplayName = "BR-OTEL-006 — /otel down is a no-op when state is NotRunning")]
    public async Task Down_NoOp_When_NotRunning()
    {
        var lifecycle = new FakeLifecycle
        {
            ProbeResult = new LifecycleStatus(LifecycleState.NotRunning, null, 13133, "no PID file, port free"),
        };
        using var factory = FactoryWith(lifecycle);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "down"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("already down", text);
        Assert.Equal(0, lifecycle.StopCalls);
    }

    [Fact(DisplayName = "BR-OTEL-006 — /otel down sweeps zombies when state is Zombie")]
    public async Task Down_Sweeps_When_Zombie()
    {
        var lifecycle = new FakeLifecycle
        {
            ProbeResult = new LifecycleStatus(LifecycleState.Zombie, 9999, 13133, "stale PID file"),
        };
        using var factory = FactoryWith(lifecycle);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "down"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("zombie", text);
        Assert.Equal(1, lifecycle.SweepCalls);
        Assert.Equal(0, lifecycle.StopCalls);  // sweep covers it
    }

    [Fact(DisplayName = "BR-OTEL-006 — /otel down refuses to kill on Conflict (BR-SECURITY-003)")]
    public async Task Down_Refuses_On_Conflict()
    {
        var lifecycle = new FakeLifecycle
        {
            ProbeResult = new LifecycleStatus(LifecycleState.Conflict, null, 13133, "port held by another process"),
        };
        using var factory = FactoryWith(lifecycle);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel/dispatch",
            FormContent(("session_id", "s1"), ("args", "down"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("cannot be stopped", text);
        Assert.Contains("BR-SECURITY-003", text);
        Assert.Equal(0, lifecycle.StopCalls);
    }

    // -------------------------------------------------------------- harness

    private static FormUrlEncodedContent FormContent(params (string K, string V)[] kv)
    {
        var c = new FormUrlEncodedContent(kv.Select(p => new KeyValuePair<string, string>(p.K, p.V)));
        c.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return c;
    }

    private static WebApplicationFactory<Program> FactoryWith(IProcessLifecycle lifecycle)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IProcessLifecycle>();
                s.AddSingleton(lifecycle);
            }));

    private sealed class FakeLifecycle : IProcessLifecycle
    {
        public LifecycleStatus ProbeResult { get; set; } = new(LifecycleState.NotRunning, null, 13133, "");
        public SpawnResult SpawnResult { get; set; } = new(true, 1, "ok");
        public bool StopResult { get; set; } = true;

        public int SpawnCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int SweepCalls { get; private set; }
        public string? LastSpawnComponent { get; private set; }
        public string? LastStopComponent { get; private set; }

        public LifecycleStatus Probe(string componentName) => ProbeResult;

        public Task<int> SweepZombiesAsync(string componentName, CancellationToken ct = default)
        {
            SweepCalls++;
            return Task.FromResult(0);
        }

        public Task<SpawnResult> SpawnAsync(string componentName, CancellationToken ct = default)
        {
            SpawnCalls++;
            LastSpawnComponent = componentName;
            return Task.FromResult(SpawnResult);
        }

        public Task<bool> StopAsync(string componentName, TimeSpan grace = default, CancellationToken ct = default)
        {
            StopCalls++;
            LastStopComponent = componentName;
            return Task.FromResult(StopResult);
        }
    }
}
