namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Settings for the helpers sidecar's own deployment shape and
/// container packaging. Bound from the <c>Sidecar</c> section of
/// appsettings.json.
///
/// Per <c>BR-OTEL-007</c>'s sibling discipline applied to the
/// sidecar tier and per <c>BR-HELPERS-002</c>'s "skills install
/// themselves correctly" clause, every per-deployment setting is
/// a typed property here — never an inline literal.
///
/// Default mode is <see cref="SidecarMode.Direct"/>; the user
/// opts into <see cref="SidecarMode.Container"/> via
/// <c>/skill-bootstrap set-mode container</c>, which walks the
/// install dance (pre-conditions → HITL confirmation →
/// rewriter applies the dependent-surface changes →
/// lifecycle swap).
/// </summary>
public sealed class SidecarOptions
{
    public const string SectionName = "Sidecar";

    public SidecarMode Mode { get; set; } = SidecarMode.Direct;

    public SidecarContainerOptions Container { get; set; } = new();
}

public enum SidecarMode
{
    /// <summary>
    /// Sidecar runs as a host process (default). Per
    /// <c>BR-HELPERS-002</c> binds <c>127.0.0.1</c> directly.
    /// </summary>
    Direct = 0,

    /// <summary>
    /// Sidecar runs inside a Docker container; the host reaches
    /// it at <c>127.0.0.1:{HostPort}</c> via port mapping. Per
    /// the amended <c>BR-HELPERS-002</c>, the sidecar inside the
    /// container binds <c>0.0.0.0:5050</c> (necessary for the
    /// port mapping); the host loopback contract is preserved.
    /// </summary>
    Container = 1,
}

public sealed class SidecarContainerOptions
{
    public string Image { get; set; } = "claude-helpers-sidecar:dev";

    public int HostPort { get; set; } = 5050;

    /// <summary>
    /// Host path of <c>persistent-enrichments.json</c> mounted
    /// into the container so <c>BR-ENRICH-009</c>'s
    /// "file is the source of truth" invariant survives
    /// container restart. Resolved relative to the working
    /// directory at run time.
    /// </summary>
    public string PersistentEnrichmentsHostPath { get; set; } = "./persistent-enrichments.json";

    /// <summary>
    /// Container name suffix appended to
    /// <c>claude-helpers-sidecar-</c> when running. Lets the
    /// rewriter scope <c>docker stop</c> / <c>docker rm</c>
    /// commands by name prefix per <c>BR-SKILL-009</c>.
    /// </summary>
    public string NameSuffix { get; set; } = "main";
}
