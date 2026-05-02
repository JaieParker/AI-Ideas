using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Infrastructure;

/// <summary>
/// BR-PROCESS-008 — process lifecycle state machine and zombie sweep.
/// Tests scope to the lifecycle domain per BR-PROCESS-007: the file
/// system is mocked via temp directories scoped to each test, the port
/// probe is mocked via <see cref="FakePortProbe"/>, and the registry
/// is constructed inline.
/// </summary>
public class ProcessLifecycleTests : IDisposable
{
    private readonly string _runtimeDir;
    private readonly string _pidFile;
    private readonly ComponentRegistry _registry;

    public ProcessLifecycleTests()
    {
        _runtimeDir = Path.Combine(Path.GetTempPath(), $"otel-lc-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_runtimeDir);
        _pidFile = Path.Combine(_runtimeDir, "sidecar.pid");
        _registry = new ComponentRegistry(new Dictionary<string, ComponentSpec>
        {
            ["sidecar"] = new ComponentSpec(
                Name: "sidecar",
                Port: 5050,
                PidFile: _pidFile,
                ExePath: "fake.dll",
                Args: Array.Empty<string>()),
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_runtimeDir, recursive: true); } catch { /* noise */ }
    }

    [Fact(DisplayName = "BR-PROCESS-008 — no PID file + port free → NotRunning")]
    public void NotRunning_When_No_PidFile_And_Port_Free()
    {
        var lifecycle = new ProcessLifecycle(new FakePortProbe(), _registry);

        var status = lifecycle.Probe("sidecar");

        Assert.Equal(LifecycleState.NotRunning, status.State);
        Assert.Null(status.Pid);
        Assert.Equal(5050, status.Port);
    }

    [Fact(DisplayName = "BR-PROCESS-008 — no PID file + port held → Conflict")]
    public void Conflict_When_No_PidFile_But_Port_Held()
    {
        var ports = new FakePortProbe { Listening = { 5050 } };
        var lifecycle = new ProcessLifecycle(ports, _registry);

        var status = lifecycle.Probe("sidecar");

        Assert.Equal(LifecycleState.Conflict, status.State);
        Assert.Contains("owner unknown", status.Reason);
    }

    [Fact(DisplayName = "BR-PROCESS-008 — PID file with alive PID + port held → RunningOurs")]
    public void RunningOurs_When_PidFile_Alive_And_Port_Held()
    {
        // Use this test's own PID — it's alive by definition.
        File.WriteAllText(_pidFile, Environment.ProcessId.ToString());
        var ports = new FakePortProbe { Listening = { 5050 } };
        var lifecycle = new ProcessLifecycle(ports, _registry);

        var status = lifecycle.Probe("sidecar");

        Assert.Equal(LifecycleState.RunningOurs, status.State);
        Assert.Equal(Environment.ProcessId, status.Pid);
    }

    [Fact(DisplayName = "BR-PROCESS-008 — PID file with dead PID + port free → Zombie (stale PID file)")]
    public void Zombie_When_Stale_PidFile_And_Port_Free()
    {
        File.WriteAllText(_pidFile, "999999");           // unlikely to be alive
        var lifecycle = new ProcessLifecycle(new FakePortProbe(), _registry);

        var status = lifecycle.Probe("sidecar");

        Assert.Equal(LifecycleState.Zombie, status.State);
        Assert.Equal(999999, status.Pid);
    }

    [Fact(DisplayName = "BR-PROCESS-008 — PID file with dead PID + port held by other → Conflict")]
    public void Conflict_When_Stale_PidFile_And_Port_Held_By_Other()
    {
        File.WriteAllText(_pidFile, "999999");
        var ports = new FakePortProbe { Listening = { 5050 } };
        var lifecycle = new ProcessLifecycle(ports, _registry);

        var status = lifecycle.Probe("sidecar");

        Assert.Equal(LifecycleState.Conflict, status.State);
        Assert.Contains("held by someone else", status.Reason);
    }

    [Fact(DisplayName = "BR-PROCESS-008 — sweep deletes stale PID file when state is Zombie")]
    public async Task Sweep_Deletes_Stale_PidFile()
    {
        File.WriteAllText(_pidFile, "999999");
        var lifecycle = new ProcessLifecycle(new FakePortProbe(), _registry);

        var killed = await lifecycle.SweepZombiesAsync("sidecar");

        Assert.False(File.Exists(_pidFile));
        Assert.Equal(0, killed); // PID was already dead, nothing to kill
    }

    [Fact(DisplayName = "BR-PROCESS-008 — sweep is a no-op when state is RunningOurs")]
    public async Task Sweep_NoOp_When_RunningOurs()
    {
        File.WriteAllText(_pidFile, Environment.ProcessId.ToString());
        var ports = new FakePortProbe { Listening = { 5050 } };
        var lifecycle = new ProcessLifecycle(ports, _registry);

        var killed = await lifecycle.SweepZombiesAsync("sidecar");

        Assert.True(File.Exists(_pidFile));  // not deleted
        Assert.Equal(0, killed);
    }

    [Fact(DisplayName = "BR-PROCESS-008 — sweep is a no-op when state is Conflict (BR-SECURITY-003)")]
    public async Task Sweep_NoOp_When_Conflict()
    {
        // Stale PID file + port held → Conflict; sweep must NOT touch the
        // process listening on the port (we can't identify it as ours).
        File.WriteAllText(_pidFile, "999999");
        var ports = new FakePortProbe { Listening = { 5050 } };
        var lifecycle = new ProcessLifecycle(ports, _registry);

        var killed = await lifecycle.SweepZombiesAsync("sidecar");

        Assert.Equal(0, killed);
        // PID file still present — sweep should not touch it on Conflict
        // because the real owner is something we don't recognize and
        // BR-SECURITY-003 forbids auto-killing unidentified processes.
        Assert.True(File.Exists(_pidFile));
    }

    [Fact(DisplayName = "BR-PROCESS-008 — Probe throws on unknown component name")]
    public void Probe_Throws_On_Unknown_Component()
    {
        var lifecycle = new ProcessLifecycle(new FakePortProbe(), _registry);
        var ex = Assert.Throws<KeyNotFoundException>(() => lifecycle.Probe("unknown-component"));
        Assert.Contains("unknown component", ex.Message);
    }

    private sealed class FakePortProbe : IPortProbe
    {
        public HashSet<int> Listening { get; } = new();
        public bool IsListening(int port) => Listening.Contains(port);
    }
}
