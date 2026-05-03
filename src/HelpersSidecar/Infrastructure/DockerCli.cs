using System.Diagnostics;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Production <see cref="IDockerCli"/> — shells out to the
/// docker binary on PATH. Per <c>BR-SKILL-008</c>, this class
/// is only constructed when the project is in
/// <see cref="SidecarMode.Container"/>; in
/// <see cref="SidecarMode.Direct"/> docker isn't invoked.
/// </summary>
public sealed class DockerCli : IDockerCli
{
    public Task<DockerResult> RunDetachedAsync(string image, int hostPort, string containerName,
        string persistentEnrichmentsHostPath, CancellationToken ct = default)
    {
        var hostPath = Path.GetFullPath(persistentEnrichmentsHostPath);
        var args = new[]
        {
            "run", "-d",
            "--name", containerName,
            "-p", $"127.0.0.1:{hostPort}:5050",
            "-v", $"{hostPath}:/app/persistent-enrichments.json",
            image,
        };
        return RunAsync(args, ct);
    }

    public async Task<DockerResult> StopAndRemoveAsync(string containerName, CancellationToken ct = default)
    {
        var stop = await RunAsync(new[] { "stop", containerName }, ct);
        // Best-effort remove even if stop failed (already-stopped case).
        var rm = await RunAsync(new[] { "rm", "-f", containerName }, ct);

        // Stop failure with rm success is fine (container was already stopped);
        // surface a combined exit so the caller sees stderr from both.
        var combinedExit = stop.Succeeded || rm.Succeeded ? 0 : 1;
        return new DockerResult(combinedExit,
            $"stop: {stop.Stdout}\nrm: {rm.Stdout}",
            $"stop: {stop.Stderr}\nrm: {rm.Stderr}");
    }

    public Task<DockerResult> PsAsync(string containerName, CancellationToken ct = default) =>
        RunAsync(new[] { "ps", "--filter", $"name={containerName}", "--format", "{{.ID}}" }, ct);

    public Task<DockerResult> VersionAsync(CancellationToken ct = default) =>
        RunAsync(new[] { "--version" }, ct);

    private static async Task<DockerResult> RunAsync(string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi)!;
            var stdout = await p.StandardOutput.ReadToEndAsync(ct);
            var stderr = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return new DockerResult(p.ExitCode, stdout.Trim(), stderr.Trim());
        }
        catch (Exception ex)
        {
            return new DockerResult(127, string.Empty, $"docker not invokable: {ex.Message}");
        }
    }
}
