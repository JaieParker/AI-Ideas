using HelpersSidecar.Domain;

namespace HelpersSidecar.Tests.Domain;

/// <summary>
/// BR-EXTEND-006 — the OTEL domain populates every IDomain
/// slice; the values match this repo's existing conventions.
/// Drift in any slice (someone changes a commit prefix or a
/// governed glob without updating the domain) gets caught here.
/// </summary>
public class OtelDomainTests
{
    // Typed as IDomain so default-implemented members (Probe,
    // PorousBoundaries) are reachable through the interface.
    private readonly IDomain _otel = new OtelDomain();

    [Fact(DisplayName = "BR-EXTEND-006 — Name is 'otel'")]
    public void Name_Is_Otel() => Assert.Equal("otel", _otel.Name);

    [Fact(DisplayName = "BR-EXTEND-006 — PlanFiles pattern matches existing repo convention")]
    public void PlanFiles_Pattern_Matches_Repo()
    {
        Assert.Equal("The-OTEL-Plan-{n}-{slug}.md", _otel.PlanFiles.Pattern);
        Assert.Equal(1, _otel.PlanFiles.NumberFloor);

        // Sanity-check the pattern interpolation works.
        Assert.Equal(
            "The-OTEL-Plan-7-domain-interface.md",
            _otel.PlanFiles.FileNameFor(7, "domain-interface"));
    }

    [Fact(DisplayName = "BR-EXTEND-006 / BR-EXTEND-002 — commit prefixes match the documented per-phase convention")]
    public void Commit_Prefixes_Match_Convention()
    {
        Assert.Equal("plan: ",        _otel.Commits.PrefixFor(ExtendPhase.Plan));
        Assert.Equal("feat(otel): ",  _otel.Commits.PrefixFor(ExtendPhase.Implement));
        Assert.Equal("chore: ",       _otel.Commits.PrefixFor(ExtendPhase.Build));
        Assert.Equal("test: ",        _otel.Commits.PrefixFor(ExtendPhase.Test));
    }

    [Fact(DisplayName = "BR-EXTEND-006 / BR-PROCESS-001 — governed globs include skills + dispatch endpoints + verb classes")]
    public void Governed_Globs_Match_Process_001()
    {
        Assert.Contains(".claude/skills/**", _otel.GovernedGlobs);
        Assert.Contains("src/HelpersSidecar/Endpoints/*DispatchEndpoint.cs", _otel.GovernedGlobs);
        Assert.Contains("src/HelpersSidecar/Application/*Verb.cs", _otel.GovernedGlobs);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — Glossary includes the project's ubiquitous language")]
    public void Glossary_Has_Core_Terms()
    {
        var terms = new[] { "session", "enrichment", "persistent", "per-session", "ticket.id", "sidecar", "collector" };
        foreach (var term in terms)
        {
            Assert.True(_otel.Glossary.ContainsKey(term),
                $"glossary missing term '{term}' — every IDomain must expose its ubiquitous language");
        }
    }

    [Fact(DisplayName = "BR-EXTEND-006 — BusinessRulesPath points at the project's BR document")]
    public void BusinessRulesPath_Is_Project_Root()
    {
        Assert.Equal("docs/business-rules.md", _otel.BusinessRulesPath);
    }

    [Fact(DisplayName = "BR-EXTEND-008 — TrustedReferences includes the seeded curated sources (Fowler + Evans + Microsoft + OTel)")]
    public void TrustedReferences_Include_Seeded_Sources()
    {
        var titles = _otel.TrustedReferences.Select(r => r.Title).ToList();

        Assert.Contains(titles, t => t.Contains("Bounded Context", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("Ubiquitous Language", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("Context Map", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("DDD Reference", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("Microsoft", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("OpenTelemetry Specification", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("OTLP", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "BR-EXTEND-008 — every TrustedReference has Title, Url, and a non-empty Why")]
    public void TrustedReferences_Are_Well_Formed()
    {
        foreach (var r in _otel.TrustedReferences)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Title), $"empty title on {r.Url}");
            Assert.False(string.IsNullOrWhiteSpace(r.Why),
                $"BR-EXTEND-008 — every TrustedReference must have a Why; {r.Title} has none");
            Assert.True(r.Url.IsAbsoluteUri, $"{r.Title} has a non-absolute URL: {r.Url}");
        }
    }

    [Fact(DisplayName = "BR-EXTEND-008 — TrustedReference URLs are unique within a domain")]
    public void TrustedReferences_Are_Unique()
    {
        var urls = _otel.TrustedReferences.Select(r => r.Url.ToString()).ToList();
        Assert.Equal(urls.Count, urls.Distinct().Count());
    }

    [Fact(DisplayName = "BR-EXTEND-006 — Probe defaults to Unknown when not overridden")]
    public void Probe_Defaults_To_Unknown()
    {
        var health = _otel.Probe();
        // OtelDomain doesn't override Probe; default-impl returns Unknown.
        Assert.Equal(DomainHealthStatus.Unknown, health.Status);
    }
}
