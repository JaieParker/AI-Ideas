namespace HelpersSidecar.Domain;

/// <summary>
/// Self-diagnostic snapshot from <see cref="IDomain.Probe"/>.
/// Default implementation returns <see cref="Unknown"/>; domains
/// that genuinely have a self-check (e.g. file integrity, port
/// reachability) override.
/// </summary>
public enum DomainHealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Failing,
}

public sealed record DomainHealth(
    DomainHealthStatus Status,
    string Reason)
{
    public static readonly DomainHealth Unknown =
        new(DomainHealthStatus.Unknown, "no probe implemented");

    public static DomainHealth Healthy(string reason = "ok") =>
        new(DomainHealthStatus.Healthy, reason);
}
