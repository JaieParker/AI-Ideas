using System.Diagnostics;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Production <see cref="IProcessLifecycle"/>. Cross-platform —
/// uses <see cref="Process.GetProcessById"/> + a PID file +
/// <see cref="IPortProbe"/>. No P/Invoke, no shell-out for
/// owner-by-port lookup; we identify our processes by PID file,
/// not by port-table inspection.
///
/// State machine:
///   no PID file + port free          → NotRunning
///   no PID file + port held          → Conflict
///   PID file + PID alive + port held → RunningOurs
///   PID file + PID alive + no port   → Zombie  (process exists but isn't bound — sweep)
///   PID file + PID dead  + port held → Conflict (PID file is stale; port belongs to someone else)
///   PID file + PID dead  + port free → Zombie  (just clean up the stale file)
/// </summary>
public sealed class ProcessLifecycle : IProcessLifecycle
{
    private readonly IPortProbe _ports;
    private readonly IComponentRegistry _registry;

    public ProcessLifecycle(IPortProbe ports, IComponentRegistry registry)
    {
        _ports = ports;
        _registry = registry;
    }

    public LifecycleStatus Probe(string componentName)
    {
        var spec = _registry.Get(componentName);
        var portHeld = _ports.IsListening(spec.Port);
        var pidFromFile = TryReadPidFile(spec.PidFile);

        if (pidFromFile is null)
        {
            return portHeld
                ? new LifecycleStatus(LifecycleState.Conflict, null, spec.Port,
                    $"port {spec.Port} is held but no PID file at {spec.PidFile} — owner unknown")
                : new LifecycleStatus(LifecycleState.NotRunning, null, spec.Port,
                    "no PID file, port free");
        }

        var alive = IsProcessAlive(pidFromFile.Value);

        return (alive, portHeld) switch
        {
            (true,  true)  => new LifecycleStatus(LifecycleState.RunningOurs, pidFromFile, spec.Port,
                $"PID {pidFromFile} alive and bound to {spec.Port}"),
            (true,  false) => new LifecycleStatus(LifecycleState.Zombie, pidFromFile, spec.Port,
                $"PID {pidFromFile} alive but not bound to {spec.Port} — sweep candidate"),
            (false, true)  => new LifecycleStatus(LifecycleState.Conflict, pidFromFile, spec.Port,
                $"PID file points at dead PID {pidFromFile} but {spec.Port} is held by someone else"),
            (false, false) => new LifecycleStatus(LifecycleState.Zombie, pidFromFile, spec.Port,
                $"stale PID file (PID {pidFromFile} dead) — sweep candidate"),
        };
    }

    public async Task<int> SweepZombiesAsync(string componentName, CancellationToken ct = default)
    {
        var spec = _registry.Get(componentName);
        var status = Probe(componentName);
        if (status.State != LifecycleState.Zombie) return 0;

        var killed = 0;
        if (status.Pid is int pid && IsProcessAlive(pid))
        {
            try
            {
                var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync(ct);
                killed = 1;
            }
            catch (ArgumentException) { /* gone already */ }
            catch (InvalidOperationException) { /* gone already */ }
        }

        TryDeletePidFile(spec.PidFile);
        return killed;
    }

    private static int? TryReadPidFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path).Trim();
            return int.TryParse(text, out var pid) ? pid : null;
        }
        catch (IOException)     { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)         { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static void TryDeletePidFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* leave it; next probe will sweep again */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }
}
