using HelpersSidecar.Application;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-EXTEND-011 — domain-scoped integration testing. The scope
/// resolver maps changed file paths → impacted domain names so a
/// CI run can scope integration tests to the domains actually
/// touched. Cross-domain changes conservatively trigger every
/// known domain.
/// </summary>
public class DomainImpactScopeTests
{
    private static IDomainResolver Resolver(params IDomain[] domains) =>
        new DomainResolver(domains);

    private static IDomain Otel() => new OtelDomain();
    private static IDomain Fake(string name, string planDir, params string[] globs) =>
        new FakeDomain(name, planDir, globs);

    [Fact(DisplayName = "BR-EXTEND-011 — change inside a domain's plan dir scopes to that domain only")]
    public void Plan_Dir_Scopes_To_Single_Domain()
    {
        var scope = new DomainImpactScope(Resolver(Otel(), Fake("kai-platform", "docs/kai-platform/plans")));

        var r = scope.ResolveImpactedDomains(new[] { "docs/otel/plans/The-OTEL-Plan-2-go-collector.md" });

        Assert.Single(r.ImpactedDomains);
        Assert.Contains("otel", r.ImpactedDomains);
        Assert.False(r.CrossDomainTriggered);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — change inside a domain's governed glob scopes to that domain")]
    public void Governed_Glob_Scopes_To_Domain()
    {
        var scope = new DomainImpactScope(Resolver(Otel()));

        var r = scope.ResolveImpactedDomains(new[] { ".claude/skills/otel/SKILL.md" });

        Assert.Contains("otel", r.ImpactedDomains);
        Assert.False(r.CrossDomainTriggered);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — change under docs/cross-domain triggers every known domain")]
    public void Cross_Domain_Path_Triggers_All_Domains()
    {
        var scope = new DomainImpactScope(Resolver(Otel(), Fake("kai-platform", "docs/kai-platform/plans")));

        var r = scope.ResolveImpactedDomains(new[] { "docs/cross-domain/plans/The-Cross-Plan.md" });

        Assert.True(r.CrossDomainTriggered);
        Assert.Contains("otel", r.ImpactedDomains);
        Assert.Contains("kai-platform", r.ImpactedDomains);
    }

    [Theory(DisplayName = "BR-EXTEND-011 — top-level cross-domain files trigger all domains")]
    [InlineData("docs/business-rules.md")]
    [InlineData("docs/process-incidents.md")]
    [InlineData("CLAUDE.md")]
    public void TopLevel_CrossDomain_Files_Trigger_All(string changed)
    {
        var scope = new DomainImpactScope(Resolver(Otel(), Fake("kai-platform", "docs/kai-platform/plans")));

        var r = scope.ResolveImpactedDomains(new[] { changed });

        Assert.True(r.CrossDomainTriggered);
        Assert.Equal(2, r.ImpactedDomains.Count);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — change matching no domain is treated as cross-domain (over-test, never under-test)")]
    public void Unmatched_Path_Is_Treated_Cross_Domain()
    {
        var scope = new DomainImpactScope(Resolver(Otel(), Fake("kai-platform", "docs/kai-platform/plans")));

        var r = scope.ResolveImpactedDomains(new[] { "tools/random-tool.py" });

        Assert.True(r.CrossDomainTriggered);
        Assert.Contains("otel", r.ImpactedDomains);
        Assert.Contains("kai-platform", r.ImpactedDomains);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — multiple changed paths union into the impacted set")]
    public void Multiple_Paths_Union_Into_Impacted_Set()
    {
        var scope = new DomainImpactScope(Resolver(Otel(), Fake("kai-platform", "docs/kai-platform/plans")));

        var r = scope.ResolveImpactedDomains(new[]
        {
            "docs/otel/plans/The-OTEL-Plan-2-go-collector.md",
            "docs/kai-platform/plans/The-KaiPlatform-Plan.md",
        });

        Assert.False(r.CrossDomainTriggered);
        Assert.Equal(2, r.ImpactedDomains.Count);
        Assert.Contains("otel", r.ImpactedDomains);
        Assert.Contains("kai-platform", r.ImpactedDomains);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — empty change list returns empty impact (no over-trigger)")]
    public void Empty_Change_List_Returns_Empty_Impact()
    {
        var scope = new DomainImpactScope(Resolver(Otel()));

        var r = scope.ResolveImpactedDomains(Array.Empty<string>());

        Assert.Empty(r.ImpactedDomains);
        Assert.False(r.CrossDomainTriggered);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — Windows-style backslash paths are normalised before matching")]
    public void Backslash_Paths_Normalised()
    {
        var scope = new DomainImpactScope(Resolver(Otel()));

        var r = scope.ResolveImpactedDomains(new[] { @"docs\otel\plans\The-OTEL-Plan.md" });

        Assert.Contains("otel", r.ImpactedDomains);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — ImpactedDomains is sorted, distinct")]
    public void Result_Is_Sorted_And_Distinct()
    {
        var scope = new DomainImpactScope(Resolver(Otel(), Fake("kai-platform", "docs/kai-platform/plans")));

        var r = scope.ResolveImpactedDomains(new[]
        {
            "docs/cross-domain/plans/X.md",
            "docs/business-rules.md",
        });

        Assert.Equal(new[] { "kai-platform", "otel" }, r.ImpactedDomains);
    }

    private sealed class FakeDomain : IDomain
    {
        public FakeDomain(string name, string planDir, IReadOnlyList<string> globs)
        {
            Name = name;
            PlanFiles = new PlanFileConventions("Fake-Plan", 1, planDir);
            GovernedGlobs = globs.Count > 0 ? globs : Array.Empty<string>();
        }

        public string Name { get; }
        public PlanFileConventions PlanFiles { get; }
        public CommitConventions Commits { get; } = new(new Dictionary<ExtendPhase, string>
        {
            [ExtendPhase.Plan] = "plan: ",
            [ExtendPhase.Implement] = "feat: ",
            [ExtendPhase.Build] = "chore: ",
            [ExtendPhase.Test] = "test: ",
        });
        public IReadOnlyList<string> GovernedGlobs { get; }
        public string PlaybookPath => $"docs/{Name}/playbook.md";
        public IReadOnlyDictionary<string, string> Glossary => new Dictionary<string, string>();
        public string BusinessRulesPath => "docs/business-rules.md";
        public IReadOnlyList<TrustedReference> TrustedReferences => Array.Empty<TrustedReference>();
    }
}
