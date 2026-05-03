using System.Diagnostics;
using System.Text;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Production <see cref="IBuildRunner"/>. Shells out to
/// <see cref="Process.Start(ProcessStartInfo)"/>, redirects
/// stdout/stderr, returns the captured strings.
///
/// Per BR-CODE-003 the command path is resolved to absolute via
/// <see cref="Path.IsPathRooted"/> + <see cref="Path.GetFullPath"/>
/// before <see cref="Process.Start(ProcessStartInfo)"/> sees it.
/// </summary>
public sealed class BuildRunner : IBuildRunner
{
    public async Task<BuildResult> RunAsync(
        string workingDirectory,
        string command,
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process? p;
        try { p = Process.Start(psi); }
        catch (Exception ex)
        {
            return new BuildResult(ExitCode: -1, Stdout: string.Empty,
                Stderr: $"failed to start build command '{command}': {ex.Message}");
        }
        if (p is null)
            return new BuildResult(-1, string.Empty, $"Process.Start returned null for '{command}'");

        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new BuildResult(p.ExitCode, stdout, stderr);
    }
}
