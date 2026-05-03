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
public sealed class ProcessLifecycle : IProcessLifecycle, IStageableLifecycle
{
    private readonly IPortProbe _ports;
    private readonly IComponentRegistry _registry;
    private readonly IBuildRunner _build;
    private readonly IHealthChecker _health;

    public ProcessLifecycle(IPortProbe ports, IComponentRegistry registry,
        IBuildRunner? build = null, IHealthChecker? health = null)
    {
        _ports = ports;
        _registry = registry;
        _build = build ?? new BuildRunner();
        _health = health ?? new HttpHealthChecker();
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

        // BR-OTEL-007 — apply per-spec environment variables to the
        // child process. Used today to propagate
        // CLAUDE_OTEL_OTLP_HTTP_PORT to the spawned Go collector;
        // the spec is generic so future tier-managed components
        // can declare any env vars they need.
        if (spec.EnvironmentVariables is not null)
        {
            foreach (var kv in spec.EnvironmentVariables)
                psi.Environment[kv.Key] = kv.Value;
        }

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

    // ============================================================
    // Stage / Promote / Discard (BR-PROCESS-011, BR-PROCESS-012)
    // ============================================================

    public async Task<StageResult> StageAsync(string componentName, CancellationToken ct = default)
    {
        var spec = _registry.Get(componentName);
        if (spec.Staging is null)
            return new StageResult(StageOutcome.NotStageable, null,
                $"component '{componentName}' has no StagingSpec");

        // Refuse to stage over a running green.
        if (IsGreenAlive(spec.Staging))
            return new StageResult(StageOutcome.AlreadyStaged, null,
                $"green is already running on :{spec.Staging.StagingPort}; discard before re-staging");

        // 1. Build to staging output.
        var build = await _build.RunAsync(
            workingDirectory: Directory.GetCurrentDirectory(),
            command: spec.Staging.BuildCommand,
            args: spec.Staging.BuildArgs,
            ct: ct);

        if (!build.Succeeded)
            return new StageResult(StageOutcome.BuildFailed, null,
                $"build exited {build.ExitCode}",
                BuildStdout: build.Stdout, BuildStderr: build.Stderr);

        // 2. Spawn green via SpawnCommand + SpawnArgs (e.g. "dotnet"
        //    + "<staging-dll-path>" for .NET sidecar; or just an .exe
        //    with empty args for a Go binary).
        var psi = new ProcessStartInfo
        {
            FileName = spec.Staging.SpawnCommand,
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in spec.Staging.SpawnArgs) psi.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(psi)!; }
        catch (Exception ex)
        {
            return new StageResult(StageOutcome.BuildFailed, null,
                $"failed to spawn green: {ex.Message}",
                BuildStdout: build.Stdout, BuildStderr: build.Stderr);
        }

        // 3. Write green PID file.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(spec.Staging.StagingPidFile) ?? ".");
            await File.WriteAllTextAsync(spec.Staging.StagingPidFile, p.Id.ToString(), ct);
        }
        catch (IOException) { /* tolerate; sweep on next stage */ }

        // 4. Poll green health.
        var healthy = await _health.WaitUntilHealthyAsync(
            port: spec.Staging.StagingPort,
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(250),
            ct: ct);

        if (!healthy)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* tolerate */ }
            TryDeletePidFile(spec.Staging.StagingPidFile);
            return new StageResult(StageOutcome.HealthCheckFailed, null,
                $"green spawned (PID {p.Id}) but :{spec.Staging.StagingPort}/healthz didn't respond within 30s");
        }

        return new StageResult(StageOutcome.Staged, p.Id,
            $"green running on :{spec.Staging.StagingPort} (PID {p.Id})");
    }

    public async Task<PromoteResult> PromoteAsync(string componentName, CancellationToken ct = default)
    {
        var spec = _registry.Get(componentName);
        if (spec.Staging is null)
            return new PromoteResult(PromoteOutcome.NotStageable, null,
                $"component '{componentName}' has no StagingSpec");

        // 1. Green must exist + be healthy.
        var greenPid = TryReadPidFile(spec.Staging.StagingPidFile);
        if (greenPid is null || !IsProcessAlive(greenPid.Value))
            return new PromoteResult(PromoteOutcome.NoGreenStaged, null,
                "no green is staged; run stage first");

        var greenHealthy = await _health.WaitUntilHealthyAsync(
            port: spec.Staging.StagingPort,
            timeout: TimeSpan.FromSeconds(2),
            interval: TimeSpan.FromMilliseconds(100),
            ct: ct);
        if (!greenHealthy)
            return new PromoteResult(PromoteOutcome.GreenNotHealthy, greenPid,
                $"green PID {greenPid} unhealthy at :{spec.Staging.StagingPort}/healthz");

        // 2. Snapshot blue (Option B — bin/Debug.bak/).
        var blueDir  = Path.GetDirectoryName(Path.IsPathRooted(spec.ExePath) ? spec.ExePath : Path.GetFullPath(spec.ExePath))!;
        var stageDir = spec.Staging.StagingPath;
        var bakDir   = blueDir.TrimEnd('/', '\\') + ".bak";
        SnapshotDirectory(blueDir, bakDir);

        // 3. Kill blue (if running).
        var bluePidPre = TryReadPidFile(spec.PidFile);
        if (bluePidPre is int bp && IsProcessAlive(bp))
        {
            try
            {
                var bluep = Process.GetProcessById(bp);
                bluep.Kill(entireProcessTree: true);
                await bluep.WaitForExitAsync(ct);
            }
            catch { /* tolerate */ }
            TryDeletePidFile(spec.PidFile);
        }

        // 4. Copy staged binary over blue's directory.
        try
        {
            CopyDirectory(stageDir, blueDir);
        }
        catch (Exception ex)
        {
            // Restore from snapshot, give up.
            CopyDirectory(bakDir, blueDir);
            return new PromoteResult(PromoteOutcome.RolledBack, null,
                $"file copy failed during promote: {ex.Message}; blue restored from {bakDir}");
        }

        // 5. Restart blue.
        var blueExe = Path.IsPathRooted(spec.ExePath) ? spec.ExePath : Path.GetFullPath(spec.ExePath);
        var bluePsi = new ProcessStartInfo
        {
            FileName = blueExe,
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        Process? newBlue = null;
        try { newBlue = Process.Start(bluePsi); }
        catch { /* fall through to rollback */ }

        if (newBlue is not null)
        {
            try { await File.WriteAllTextAsync(spec.PidFile, newBlue.Id.ToString(), ct); } catch { }

            var blueHealthy = await _health.WaitUntilHealthyAsync(
                port: spec.Port,
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(250),
                ct: ct);

            if (blueHealthy)
            {
                // 6. Promote complete — kill green, clean its PID file.
                try
                {
                    var gp = Process.GetProcessById(greenPid.Value);
                    gp.Kill(entireProcessTree: true);
                    await gp.WaitForExitAsync(ct);
                }
                catch { /* tolerate */ }
                TryDeletePidFile(spec.Staging.StagingPidFile);
                return new PromoteResult(PromoteOutcome.Promoted, newBlue.Id,
                    $"blue restarted from staged binary (PID {newBlue.Id}); green killed");
            }

            // Blue's restart didn't go healthy — kill it and restore.
            try { newBlue.Kill(entireProcessTree: true); } catch { }
            TryDeletePidFile(spec.PidFile);
        }

        // 7. Rollback: restore from snapshot, restart blue from original binary.
        CopyDirectory(bakDir, blueDir);
        Process? rolledBlue = null;
        try { rolledBlue = Process.Start(bluePsi); } catch { /* tolerate */ }
        if (rolledBlue is not null)
        {
            try { await File.WriteAllTextAsync(spec.PidFile, rolledBlue.Id.ToString(), ct); } catch { }
        }

        return new PromoteResult(PromoteOutcome.RolledBack,
            rolledBlue?.Id,
            "blue's restart from staged binary did not become healthy; restored from snapshot. " +
            "Green is still running on :{spec.Staging.StagingPort} for inspection.");
    }

    public async Task<DiscardResult> DiscardAsync(string componentName, CancellationToken ct = default)
    {
        var spec = _registry.Get(componentName);
        if (spec.Staging is null)
            return new DiscardResult(DiscardOutcome.NotStageable,
                $"component '{componentName}' has no StagingSpec");

        var greenPid = TryReadPidFile(spec.Staging.StagingPidFile);
        if (greenPid is null || !IsProcessAlive(greenPid.Value))
        {
            TryDeletePidFile(spec.Staging.StagingPidFile);  // clean up stale file if any
            return new DiscardResult(DiscardOutcome.NoGreenStaged, "no green to discard");
        }

        try
        {
            var p = Process.GetProcessById(greenPid.Value);
            p.Kill(entireProcessTree: true);
            await p.WaitForExitAsync(ct);
        }
        catch { /* tolerate */ }
        TryDeletePidFile(spec.Staging.StagingPidFile);
        return new DiscardResult(DiscardOutcome.Discarded, $"green PID {greenPid} killed");
    }

    private bool IsGreenAlive(StagingSpec staging)
    {
        var pid = TryReadPidFile(staging.StagingPidFile);
        return pid is int p && IsProcessAlive(p);
    }

    private static void SnapshotDirectory(string src, string dst)
    {
        try
        {
            if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
        }
        catch (IOException) { /* tolerate */ }
        try { CopyDirectory(src, dst); } catch (IOException) { /* tolerate */ }
    }

    private static void CopyDirectory(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(dst, name), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(src))
        {
            var name = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(dst, name));
        }
    }
}
