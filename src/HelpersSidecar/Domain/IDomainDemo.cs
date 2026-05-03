using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Domain;

/// <summary>
/// Optional companion contract to <see cref="IDomain"/> — a domain
/// MAY register an <see cref="IDomainDemo"/> alongside its
/// <see cref="IDomain"/> implementation to expose a guided
/// onboarding tour (BR-EXTEND-010).
///
/// The dispatch endpoint owns the platform-level pre-flight
/// (sidecar reachable, collector control, output dir, persistent
/// file, OTLP port conflict — `STEP 00.x` rows) and the teardown
/// section. Only the live skill-chain section is domain-owned —
/// that's what <see cref="RunAsync"/> returns.
///
/// A domain that has no demo simply doesn't register
/// <see cref="IDomainDemo"/>; the dispatch endpoint reports
/// "no demo for domain &lt;name&gt;" and renders pre-flight + teardown
/// only.
/// </summary>
public interface IDomainDemo
{
    /// <summary>Name of the <see cref="IDomain"/> this demo belongs to.</summary>
    string DomainName { get; }

    /// <summary>
    /// Run the live skill-chain section. Each returned
    /// <see cref="DemoStepResult"/> is rendered as
    /// <c>STEP NN: PASS|FAIL — &lt;label&gt;</c> with a detail line.
    /// </summary>
    Task<IReadOnlyList<DemoStepResult>> RunAsync(DemoContext ctx, CancellationToken ct = default);
}

/// <summary>
/// What an <see cref="IDomainDemo"/> needs from the platform: the
/// session id (for skill-chain calls) and the loopback skill
/// dispatch client.
/// </summary>
public sealed record DemoContext(string SessionId, ISkillDispatchClient Skills);

/// <summary>
/// One row of the live demo output. <see cref="Number"/> drives
/// the <c>STEP NN</c> render order; <see cref="Detail"/> is the
/// indented sub-line under the marker.
/// </summary>
public sealed record DemoStepResult(int Number, string Label, bool Pass, string Detail);
