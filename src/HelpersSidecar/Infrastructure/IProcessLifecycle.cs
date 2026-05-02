namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Manages the lifecycle of long-running components the project
/// owns (BR-PROCESS-008). Each component has a stable name in
/// <see cref="IComponentRegistry"/>; the lifecycle service
/// detects state, sweeps zombies (ours, dead, with stale PID
/// files), and refuses to act when the port is held by something
/// not ours.
///
/// The service is consumed by:
///   1. The /skill-bootstrap skill via the binary's --lifecycle
///      CLI mode (used before the sidecar is up).
///   2. Tenant skills like /otel up (future, Plan-5) via the
///      sidecar's HTTP endpoints.
/// </summary>
public interface IProcessLifecycle
{
    LifecycleStatus Probe(string componentName);
    Task<int> SweepZombiesAsync(string componentName, CancellationToken ct = default);

    /// <summary>
    /// Spawn the component. Refuses if the component is already
    /// running (state != NotRunning). Caller should sweep zombies
    /// first if needed.
    /// </summary>
    Task<SpawnResult> SpawnAsync(string componentName, CancellationToken ct = default);

    /// <summary>
    /// Stop the component if it's RunningOurs. Returns true if
    /// stopped, false if it wasn't running (no-op). Never kills a
    /// process not identified by our PID file.
    /// </summary>
    Task<bool> StopAsync(string componentName, TimeSpan grace = default, CancellationToken ct = default);
}

public sealed record SpawnResult(bool Spawned, int? Pid, string Reason);

public enum LifecycleState
{
    /// <summary>No PID file, port free. A spawn will succeed.</summary>
    NotRunning,

    /// <summary>PID file present, PID is alive, port is bound.</summary>
    RunningOurs,

    /// <summary>
    /// PID file present but PID is dead (or alive but port not
    /// bound). Sweep will clean this up.
    /// </summary>
    Zombie,

    /// <summary>
    /// Port is held but the holder isn't ours (no PID file or
    /// the file's PID is dead while the port stays held). The
    /// user must resolve — see BR-OTEL-005-style options.
    /// </summary>
    Conflict,
}

public sealed record LifecycleStatus(
    LifecycleState State,
    int? Pid,
    int Port,
    string Reason);
