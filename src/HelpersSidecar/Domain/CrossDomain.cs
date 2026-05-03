namespace HelpersSidecar.Domain;

/// <summary>
/// First-class virtual domain for plans, BRs, and incidents that
/// genuinely span domains. Plan-9 reserved <c>docs/cross-domain/plans/</c>
/// for this; the resolver registers <see cref="CrossDomain"/> as
/// <c>name="cross-domain"</c> so consumers (the multi-directory
/// scanner, the index endpoint, future cross-domain audit tooling)
/// can resolve it the same way they resolve OTEL or kai-platform.
///
/// <para>This is intentionally a thin <see cref="IDomain"/>: no
/// skill, no playbook (the cross-domain artefacts are plans only —
/// they describe boundaries between domains, not the operation of
/// any one domain), no glossary, no commits convention (commits
/// touching cross-domain plans use whichever per-domain prefix is
/// closest to the cross-cutting subject). The slice that matters
/// is <see cref="PlanFiles"/>.<see cref="PlanFileConventions.Directory"/>.</para>
///
/// <para>Per <c>BR-EXTEND-006</c>, the contract is fully populated;
/// optional members default. The empty <see cref="GovernedGlobs"/>
/// list means cross-domain is correctly NOT under <c>/extend-skills</c>'s
/// governance (changes to <c>docs/cross-domain/plans/</c> trigger
/// every domain's tests via <c>BR-EXTEND-011</c>'s top-level fallback;
/// they don't go through any single domain's flow).</para>
/// </summary>
public sealed class CrossDomain : IDomain
{
    public string Name => "cross-domain";

    public PlanFileConventions PlanFiles { get; } = new(
        Prefix: "The-Cross-Plan",
        NumberFloor: 1,
        Directory: "docs/cross-domain/plans");

    public CommitConventions Commits { get; } = new(
        new Dictionary<ExtendPhase, string>
        {
            [ExtendPhase.Plan]      = "plan: ",
            [ExtendPhase.Implement] = "feat(cross-domain): ",
            [ExtendPhase.Build]     = "chore: ",
            [ExtendPhase.Test]      = "test: ",
        });

    public IReadOnlyList<string> GovernedGlobs { get; } = Array.Empty<string>();

    public string PlaybookPath => "docs/cross-domain/playbook.md";

    public IReadOnlyDictionary<string, string> Glossary { get; } = new Dictionary<string, string>
    {
        ["cross-domain"] =
            "An artefact (plan, BR, incident) that spans two or more registered domains. Cross-domain plans live at docs/cross-domain/plans/. Cross-domain BRs and incidents stay at docs/business-rules.md / docs/process-incidents.md.",
        ["porous-boundary"] =
            "Two domains that legitimately share some language or call-paths. Each domain's IDomain.PorousBoundaries lists the other; cross-domain plans are the canonical place to record what flows across that boundary.",
    };

    public string BusinessRulesPath => "docs/business-rules.md";

    public IReadOnlyList<TrustedReference> TrustedReferences { get; } = Array.Empty<TrustedReference>();
}
