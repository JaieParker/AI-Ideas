using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Domain;

/// <summary>
/// BR-EXTEND-006 / Plan-9 — the cross-domain virtual IDomain
/// reserves docs/cross-domain/plans/ for plans that span domains.
/// Thin shape: only the slices that matter (PlanFiles.Directory,
/// glossary terms, commit conventions for the rare cross-cutting
/// commit) are populated; GovernedGlobs is empty so /extend-skills
/// doesn't try to govern it.
/// </summary>
public class CrossDomainTests
{
    private readonly IDomain _xd = new CrossDomain();

    [Fact(DisplayName = "BR-EXTEND-006 — CrossDomain.Name is 'cross-domain'")]
    public void Name_Is_Cross_Domain() => Assert.Equal("cross-domain", _xd.Name);

    [Fact(DisplayName = "BR-EXTEND-006 — CrossDomain.PlanFiles.Directory is docs/cross-domain/plans (Plan-9)")]
    public void PlanFiles_Directory_Is_CrossDomain_Plans()
    {
        Assert.Equal("docs/cross-domain/plans", _xd.PlanFiles.Directory);
        Assert.Equal("The-Cross-Plan", _xd.PlanFiles.Prefix);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — CrossDomain.GovernedGlobs is empty (not under /extend-skills governance)")]
    public void GovernedGlobs_Is_Empty()
    {
        Assert.Empty(_xd.GovernedGlobs);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — CrossDomain.PlaybookPath is docs/cross-domain/playbook.md")]
    public void PlaybookPath_Is_CrossDomain_Location()
    {
        Assert.Equal("docs/cross-domain/playbook.md", _xd.PlaybookPath);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — CrossDomain.Glossary defines 'cross-domain' and 'porous-boundary'")]
    public void Glossary_Has_Core_Cross_Domain_Terms()
    {
        Assert.True(_xd.Glossary.ContainsKey("cross-domain"));
        Assert.True(_xd.Glossary.ContainsKey("porous-boundary"));
    }

    [Fact(DisplayName = "BR-EXTEND-006 — DomainResolver registers CrossDomain alongside OtelDomain (Plan-9)")]
    public void Resolver_Knows_Both_Otel_And_Cross_Domain()
    {
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain(), new CrossDomain() });

        Assert.Contains("otel", resolver.KnownNames);
        Assert.Contains("cross-domain", resolver.KnownNames);
        Assert.IsType<CrossDomain>(resolver.ResolveOrThrow("cross-domain"));
    }

    [Fact(DisplayName = "BR-EXTEND-011 — CrossDomain registration does NOT broaden DomainImpactScope unexpectedly")]
    public void DomainImpactScope_Treats_CrossDomain_Plans_As_Cross_Domain()
    {
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain(), new CrossDomain() });
        var scope = new HelpersSidecar.Application.DomainImpactScope(resolver);

        var r = scope.ResolveImpactedDomains(new[] { "docs/cross-domain/plans/The-Cross-Plan-2-foo.md" });

        // Path matches CrossDomain.PlanFiles.Directory directly, AND lives
        // under the docs/cross-domain/ cross-domain prefix → both domains
        // are impacted (cross-domain effect persists).
        Assert.True(r.CrossDomainTriggered);
        Assert.Contains("otel", r.ImpactedDomains);
        Assert.Contains("cross-domain", r.ImpactedDomains);
    }
}
