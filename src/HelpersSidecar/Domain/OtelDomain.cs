namespace HelpersSidecar.Domain;

/// <summary>
/// Concrete OTEL domain. Populates every <see cref="IDomain"/>
/// slice with the values that match this repo's existing
/// conventions (verified by <c>OtelDomainTests</c>).
/// </summary>
/// <remarks>BR-EXTEND-006 — this is the first concrete
/// implementation. kai-platform's <c>KaiPlatformDomain</c> will
/// follow the same shape; see Plan-5's "What kai-platform
/// plugging in will look like" section.</remarks>
public sealed class OtelDomain : IDomain
{
    public string Name => "otel";

    public PlanFileConventions PlanFiles { get; } = new(
        Prefix: "The-OTEL-Plan",
        NumberFloor: 1,
        Directory: "docs/otel/plans");

    public CommitConventions Commits { get; } = new(
        new Dictionary<ExtendPhase, string>
        {
            [ExtendPhase.Plan]      = "plan: ",
            [ExtendPhase.Implement] = "feat(otel): ",
            [ExtendPhase.Build]     = "chore: ",
            [ExtendPhase.Test]      = "test: ",
        });

    public IReadOnlyList<string> GovernedGlobs { get; } = new[]
    {
        ".claude/skills/**",
        "src/HelpersSidecar/Endpoints/*DispatchEndpoint.cs",
        "src/HelpersSidecar/Application/*Verb.cs",
    };

    public string PlaybookPath => "docs/otel/playbook.md";

    public IReadOnlyDictionary<string, string> Glossary { get; } = new Dictionary<string, string>
    {
        ["session"] =
            "A Claude Code session, identified by session.id. Per-session enrichments are isolated to one session.",
        ["enrichment"] =
            "A key/value attribute stamped onto OTLP records. Two scopes: per-session (in-memory, isolated by session.id) and persistent (on-disk, applies to every session).",
        ["persistent"] =
            "Enrichment scope written to persistent-enrichments.json; survives collector restarts. Set via /otel set.",
        ["per-session"] =
            "Enrichment scope held in memory by session.id; lost on collector restart. Set via /enrich.",
        ["ticket.id"] =
            "Conventional per-session enrichment key for the work-item context (e.g. JA-0001).",
        ["sidecar"] =
            "The .NET helpers sidecar at 127.0.0.1:5050. Hosts every skill's dispatch endpoint.",
        ["collector"] =
            "The OTel collector binary. Receives OTLP on :4318, exposes control on :13133, healthz on :13134.",
        ["bootstrap-class skill"] =
            "A skill that must exist before the rule that would govern its creation can run. /otel-extend and /skill-bootstrap are the two named exceptions in BR-PROCESS-001.",
    };

    public string BusinessRulesPath => "docs/business-rules.md";

    public IReadOnlyList<TrustedReference> TrustedReferences { get; } = new[]
    {
        // Strategic design / DDD — Plan-5 + BR-EXTEND-008 seed.
        new TrustedReference(
            Title: "Bounded Context — Martin Fowler",
            Url: new Uri("https://martinfowler.com/bliki/BoundedContext.html"),
            Why:  "Canonical articulation of why software boundaries follow language boundaries. Cited by BR-SKILL-011 (planned, Plan-7).",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "Ubiquitous Language — Martin Fowler",
            Url: new Uri("https://martinfowler.com/bliki/UbiquitousLanguage.html"),
            Why:  "Why the same word may mean one thing inside a context and a different thing outside. Underlies BR-SKILL-011's third clause.",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "Context Map — Martin Fowler",
            Url: new Uri("https://martinfowler.com/bliki/ContextMap.html"),
            Why:  "The context-map artefact this project produces in docs/context-map.md (Plan-7 deliverable).",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "Domain-Driven Design tag — Martin Fowler",
            Url: new Uri("https://martinfowler.com/tags/domain%20driven%20design.html"),
            Why:  "Umbrella for Fowler's accumulated DDD writing. Discovery starting point only — individual articles are cited via their own TrustedReference rows.",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "Architecture index — Martin Fowler",
            Url: new Uri("https://martinfowler.com/architecture/"),
            Why:  "Index of Fowler's architecture writing. Used by the architecture-review agent (Plan-6) for cross-cutting questions.",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "DDD Reference (PDF) — Eric Evans",
            Url: new Uri("https://www.domainlanguage.com/wp-content/uploads/2016/05/DDD_Reference_2015-03.pdf"),
            Why:  "Free distillation of Evans' 2003 book. The canonical glossary of DDD terms.",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "Domain analysis — Microsoft Architecture Center",
            Url: new Uri("https://learn.microsoft.com/en-us/azure/architecture/microservices/model/domain-analysis"),
            Why:  "Practical lens on bounded contexts as service boundaries — closest framing to this project's existing structure.",
            AddedInPlan: "Plan-5"),

        // OpenTelemetry — domain-specific authoritative sources.
        new TrustedReference(
            Title: "OpenTelemetry Specification",
            Url: new Uri("https://opentelemetry.io/docs/specs/otel/"),
            Why:  "Authoritative source for OTLP record shape, attribute keys, and signal semantics.",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "OTLP Specification",
            Url: new Uri("https://opentelemetry.io/docs/specs/otlp/"),
            Why:  "Wire-format reference for the receiver in our collector.",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "OpenTelemetry Collector docs",
            Url: new Uri("https://opentelemetry.io/docs/collector/"),
            Why:  "Reference for receiver / processor / exporter contracts.",
            AddedInPlan: "Plan-5"),
        new TrustedReference(
            Title: "OpenTelemetry Specification (GitHub)",
            Url: new Uri("https://github.com/open-telemetry/opentelemetry-specification"),
            Why:  "Source-of-truth repo for the spec; cited when the rendered docs lag.",
            AddedInPlan: "Plan-5"),
    };
}
