namespace HelpersSidecar.Domain;

/// <summary>
/// Contract for a project domain. Each domain self-implements;
/// the project does not centralise domain knowledge in a
/// registry. Consumers (<c>/extend-skills</c>, <c>/demo</c>,
/// future <c>/architecture-review</c>) resolve domains by
/// <see cref="Name"/> through <see cref="HelpersSidecar.Infrastructure.IDomainResolver"/>.
///
/// Adding a new domain is one new class implementing this
/// interface plus one DI registration. Existing consumers do
/// not change.
///
/// The interface is the **knowledge facade** for its domain —
/// each property is a slice of authoritative information
/// scoped to the domain. Consumers compose slices; the domain
/// owns the content.
/// </summary>
/// <remarks>BR-EXTEND-006 (this contract) and BR-EXTEND-008
/// (TrustedReferences curation) govern the shape and the
/// authoring discipline.</remarks>
public interface IDomain
{
    /// <summary>
    /// Stable identifier — the first arg to <c>/extend-skills</c>
    /// and <c>/demo</c>. Lower-case, alphanumeric + dashes.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Plan-file naming pattern, e.g.
    /// <c>"The-OTEL-Plan-{n}-{slug}.md"</c>.
    /// </summary>
    PlanFileConventions PlanFiles { get; }

    /// <summary>
    /// Per-phase commit-message prefixes per BR-EXTEND-002.
    /// </summary>
    CommitConventions Commits { get; }

    /// <summary>
    /// Glob patterns for paths this domain governs. Changes
    /// inside these globs run through <c>/extend-skills</c>
    /// (BR-PROCESS-001).
    /// </summary>
    IReadOnlyList<string> GovernedGlobs { get; }

    /// <summary>
    /// Path (relative to the domain skill's directory or the
    /// project root) to this domain's flow playbook.
    /// </summary>
    string PlaybookPath { get; }

    /// <summary>
    /// Domain glossary — terms used authoritatively in this
    /// domain. Maps term → definition. Consumed by
    /// <c>/glossary</c> when it lands.
    /// </summary>
    IReadOnlyDictionary<string, string> Glossary { get; }

    /// <summary>
    /// Path (project-root relative) to the domain's business
    /// rules document. v1 returns the shared
    /// <c>docs/business-rules.md</c>; future domains may have
    /// their own.
    /// </summary>
    string BusinessRulesPath { get; }

    /// <summary>
    /// Curated authoritative external sources the domain accepts
    /// as citable (BR-EXTEND-008). The architecture-review
    /// agent's response is validated to cite only URLs that
    /// appear in the resolved domain's list (or in any other
    /// registered domain's list — references are unioned at
    /// citation time).
    /// </summary>
    IReadOnlyList<TrustedReference> TrustedReferences { get; }

    /// <summary>
    /// Optional self-diagnostic. Default: Unknown. Domains may
    /// override to expose a probe (file integrity, dependency
    /// reachability, …).
    /// </summary>
    DomainHealth Probe() => DomainHealth.Unknown;

    /// <summary>
    /// Optional list of other domain names this domain is
    /// considered "porous" with — i.e. legitimately shares some
    /// language or call-paths. Used by future cross-domain
    /// audit tooling. Default: empty (strictly bounded).
    /// </summary>
    IReadOnlyList<string> PorousBoundaries => Array.Empty<string>();
}
