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

    public async Task<SpawnResult> SpawnAsync(string componentName, CancellationToken ct = default)
    {
        var spec = _registry.Get(componentName);
        var status = Probe(componentName);

        if (status.State == LifecycleState.RunningOurs)
            return new SpawnResult(Spawned: false, Pid: status.Pid, Reason: $"already running (PID {status.Pid})");

        if (status.State == LifecycleState.Conflict)
            return new SpawnResult(Spawned: false, Pid: null, Reason: status.Reason);

        // Sweep before spawn so a Zombie state from a prior session
        // is cleaned out automatically.
        if (status.State == LifecycleState.Zombie)
            await SweepZombiesAsync(componentName, ct);

        // Resolve the exe path to absolute. Process.Start on Windows
        // doesn't reliably resolve forward-slash relative paths against
        // the working directory, so we normalise here.
        var exePath = Path.IsPathRooted(spec.ExePath)
            ? spec.ExePath
            : Path.GetFullPath(spec.ExePath);

        if (!File.Exists(exePath))
            return new SpawnResult(Spawned: false, Pid: null,
                Reason: $"exe not found: {exePath}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in spec.Args) psi.ArgumentList.Add(arg);

        Process p;
        try { p = Process.Start(psi)!; }
        catch (Exception ex)
        {
            return new SpawnResult(Spawned: false, Pid: null, Reason: $"failed to start {exePath}: {ex.Message}");
        }

        // Drain stdout/stderr to a log file so the child doesn't deadlock
        // on full pipes. The log lives next to the PID file.
        var logPath = Path.ChangeExtension(spec.PidFile, ".log");
        try { Directory.CreateDirectory(Path.GetDirectoryName(spec.PidFile)!); }
        catch (IOException) { }
        StartLogPump(p, logPath);

        // Write the PID file so future probes see RunningOurs.
        try { File.WriteAllText(spec.PidFile, p.Id.ToString()); }
        catch (IOException ex)
        {
            return new SpawnResult(Spawned: true, Pid: p.Id, Reason: $"spawned PID {p.Id} but PID file write failed: {ex.Message}");
        }

        return new SpawnResult(Spawned: true, Pid: p.Id, Reason: $"spawned {componentName} as PID {p.Id} (log: {logPath})");
    }

    public async Task<bool> StopAsync(string componentName, TimeSpan grace = default, CancellationToken ct = default)
    {
        var spec = _registry.Get(componentName);
        var status = Probe(componentName);

        // Only stop processes our PID file identifies as ours
        // (BR-SECURITY-003 — never kill unidentified processes).
        if (status.State != LifecycleState.RunningOurs) return false;

        if (status.Pid is not int pid) return false;

        try
        {
            var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            using var cts = grace > TimeSpan.Zero
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (grace > TimeSpan.Zero) cts.CancelAfter(grace);
            await p.WaitForExitAsync(cts.Token);
        }
        catch (ArgumentException) { /* already gone */ }
        catch (InvalidOperationException) { /* already gone */ }
        catch (OperationCanceledException) { /* timed out; PID file cleanup proceeds anyway */ }

        TryDeletePidFile(spec.PidFile);
        return true;
    }

    private static void StartLogPump(Process p, string logPath)
    {
        // Open the log in append mode and pump both streams into it
        // line-by-line. Background tasks; we don't await them — they end
        // when the child exits and closes its stdio.
        _ = Task.Run(async () =>
        {
            try
            {
                using var writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete));
                writer.AutoFlush = true;
                string? line;
                while ((line = await p.StandardOutput.ReadLineAsync()) is not null)
                    await writer.WriteLineAsync(line);
            }
            catch { /* tolerate */ }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                using var writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete));
                writer.AutoFlush = true;
                string? line;
                while ((line = await p.StandardError.ReadLineAsync()) is not null)
                    await writer.WriteLineAsync($"[stderr] {line}");
            }
            catch { /* tolerate */ }
        });
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
