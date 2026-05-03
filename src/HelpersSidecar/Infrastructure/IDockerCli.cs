namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Seam for docker shell-outs. Tests substitute a fake
/// <c>IDockerCli</c> per BR-PROCESS-007; production uses
/// <see cref="DockerCli"/>.
///
/// Per the amended <c>BR-SKILL-008</c>, docker is an accepted
/// non-.NET dependency only when the project is in
/// <see cref="SidecarMode.Container"/>. The lifecycle skill's
/// allowed-tools is rewritten by <see cref="ISkillRewriter"/>
/// to add or remove the docker patterns in lockstep with the
/// mode change.
/// </summary>
public interface IDockerCli
{
    /// <summary>
    /// <c>docker run -d --name &lt;name&gt; -p {HostPort}:5050
    /// -v &lt;host-persistent&gt;:/app/persistent-enrichments.json
    /// &lt;Image&gt;</c>. Detached. Returns the container ID
    /// printed by docker on success.
    /// </summary>
    Task<DockerResult> RunDetachedAsync(string image, int hostPort, string containerName,
        string persistentEnrichmentsHostPath, CancellationToken ct = default);

    /// <summary>
    /// <c>docker stop &lt;name&gt;</c> + <c>docker rm &lt;name&gt;</c>.
    /// </summary>
    Task<DockerResult> StopAndRemoveAsync(string containerName, CancellationToken ct = default);

    /// <summary>
    /// <c>docker ps --filter name=&lt;name&gt; --format &quot;{{.ID}}&quot;</c>.
    /// Returns the container id (or empty if not running).
    /// </summary>
    Task<DockerResult> PsAsync(string containerName, CancellationToken ct = default);

    /// <summary>
    /// <c>docker --version</c>. Used by pre-flight to confirm docker
    /// is installed (BR-SECURITY-003 — never auto-install).
    /// </summary>
    Task<DockerResult> VersionAsync(CancellationToken ct = default);
}

public sealed record DockerResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Succeeded => ExitCode == 0;
}
