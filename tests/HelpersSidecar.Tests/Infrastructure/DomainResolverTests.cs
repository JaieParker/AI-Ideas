using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Infrastructure;

/// <summary>
/// BR-EXTEND-006 — IDomainResolver wraps IEnumerable&lt;IDomain&gt; into
/// a name-keyed lookup. Tests cover lookup, missing-name failure,
/// uniqueness invariant, and KnownNames ordering.
/// </summary>
public class DomainResolverTests
{
    [Fact(DisplayName = "BR-EXTEND-006 — ResolveOrThrow returns the domain by Name")]
    public void Resolve_Returns_Matching_Domain()
    {
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain() });

        var d = resolver.ResolveOrThrow("otel");

        Assert.Equal("otel", d.Name);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — ResolveOrThrow throws KeyNotFoundException for unknown Name")]
    public void Resolve_Throws_For_Unknown_Name()
    {
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain() });

        var ex = Assert.Throws<KeyNotFoundException>(() => resolver.ResolveOrThrow("not-a-domain"));
        Assert.Contains("unknown domain", ex.Message);
        Assert.Contains("otel", ex.Message);  // lists known names
    }

    [Fact(DisplayName = "BR-EXTEND-006 — TryResolve returns false for unknown Name without throwing")]
    public void TryResolve_Returns_False_For_Unknown()
    {
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain() });

        var ok = resolver.TryResolve("not-a-domain", out var d);

        Assert.False(ok);
        Assert.Null(d);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — duplicate Name registration fails fast at construction")]
    public void Duplicate_Name_Throws_At_Construction()
    {
        var fakeOtel = new FakeDomain("otel");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new DomainResolver(new IDomain[] { new OtelDomain(), fakeOtel }));

        Assert.Contains("duplicate domain Name", ex.Message);
        Assert.Contains("'otel'", ex.Message);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — KnownNames lists all registered domain names alphabetically")]
    public void KnownNames_Lists_All()
    {
        var resolver = new DomainResolver(new IDomain[]
        {
            new FakeDomain("zebra"),
            new OtelDomain(),
            new FakeDomain("alpha"),
        });

        Assert.Equal(new[] { "alpha", "otel", "zebra" }, resolver.KnownNames);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — All exposes every registered IDomain")]
    public void All_Returns_Every_Domain()
    {
        var resolver = new DomainResolver(new IDomain[]
        {
            new OtelDomain(),
            new FakeDomain("foo"),
        });

        Assert.Equal(2, resolver.All.Count);
    }

    /// <summary>
    /// Minimal IDomain stub — used to test resolver invariants
    /// without depending on a real second-domain implementation
    /// (kai-platform is incubating elsewhere, not in this repo).
    /// </summary>
    private sealed class FakeDomain : IDomain
    {
        public FakeDomain(string name) => Name = name;
        public string Name { get; }
        public PlanFileConventions PlanFiles { get; } = new("Plan-{n}.md");
        public CommitConventions Commits { get; } = new(new Dictionary<ExtendPhase, string>
        {
            [ExtendPhase.Plan]      = "plan: ",
            [ExtendPhase.Implement] = "feat: ",
            [ExtendPhase.Build]     = "chore: ",
            [ExtendPhase.Test]      = "test: ",
        });
        public IReadOnlyList<string> GovernedGlobs { get; } = Array.Empty<string>();
        public string PlaybookPath => "playbook.md";
        public IReadOnlyDictionary<string, string> Glossary { get; } = new Dictionary<string, string>();
        public string BusinessRulesPath => "br.md";
        public IReadOnlyList<TrustedReference> TrustedReferences { get; } = Array.Empty<TrustedReference>();
    }
}
