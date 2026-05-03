using HelpersSidecar.Application;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// Plan-9 cross-domain discoverability — the multi-directory
/// scanner walks every registered domain's PlanFiles.Directory
/// plus the cross-domain virtual domain. Tests use a fake scanner
/// to be cwd-free and platform-agnostic.
/// </summary>
public class PlansIndexBuilderTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "BR-PROCESS-013 — index renders PLAN_INDEX v1 schema header with UTC timestamp")]
    public void Index_Renders_Schema_Header()
    {
        var builder = NewBuilder(new OtelDomain(), new CrossDomain());

        var result = builder.Build("/project", FixedTime);

        Assert.Contains("PLAN_INDEX v1 — generated 2026-05-03T12:00:00Z", result.Markdown);
        Assert.Contains("# Plans index", result.Markdown);
    }

    [Fact(DisplayName = "Plan-9 — index has one section per registered domain (sorted by name)")]
    public void Index_Has_Section_Per_Domain_Sorted()
    {
        var fakeScanner = new MultiDirScanner(new()
        {
            ["/project/docs/otel/plans"] = new[] { "The-OTEL-Plan.md", "The-OTEL-Plan-2-go-collector.md" },
            ["/project/docs/cross-domain/plans"] = new[] { "The-Cross-Plan.md" },
        });
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain(), new CrossDomain() });
        var builder = new PlansIndexBuilder(resolver, fakeScanner);

        var result = builder.Build("/project", FixedTime);

        var crossDomainIdx = result.Markdown.IndexOf("## Domain: cross-domain", StringComparison.Ordinal);
        var otelIdx = result.Markdown.IndexOf("## Domain: otel", StringComparison.Ordinal);

        Assert.True(crossDomainIdx >= 0);
        Assert.True(otelIdx >= 0);
        // Sorted ordinal: "cross-domain" < "otel"
        Assert.True(crossDomainIdx < otelIdx);
    }

    [Fact(DisplayName = "Plan-9 — index lists every matching plan as a markdown link")]
    public void Index_Lists_Plans_As_Markdown_Links()
    {
        var fakeScanner = new MultiDirScanner(new()
        {
            ["/project/docs/otel/plans"] = new[]
            {
                "The-OTEL-Plan.md",
                "The-OTEL-Plan-2-go-collector.md",
                "Some-Random.md",  // not a plan, must be filtered
            },
        });
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain() });
        var builder = new PlansIndexBuilder(resolver, fakeScanner);

        var result = builder.Build("/project", FixedTime);

        Assert.Contains("[The-OTEL-Plan.md](docs/otel/plans/The-OTEL-Plan.md)", result.Markdown);
        Assert.Contains("[The-OTEL-Plan-2-go-collector.md](docs/otel/plans/The-OTEL-Plan-2-go-collector.md)", result.Markdown);
        Assert.DoesNotContain("Some-Random.md", result.Markdown);
    }

    [Fact(DisplayName = "Plan-9 — empty per-domain dir renders '(no plans yet)'")]
    public void Empty_Domain_Renders_Placeholder()
    {
        var fakeScanner = new MultiDirScanner(new()
        {
            ["/project/docs/cross-domain/plans"] = Array.Empty<string>(),
        });
        var resolver = new DomainResolver(new IDomain[] { new CrossDomain() });
        var builder = new PlansIndexBuilder(resolver, fakeScanner);

        var result = builder.Build("/project", FixedTime);

        Assert.Contains("_(no plans yet)_", result.Markdown);
    }

    [Fact(DisplayName = "Plan-9 — PerDomainCounts and TotalPlans match listed plans")]
    public void Counts_Match_Listed_Plans()
    {
        var fakeScanner = new MultiDirScanner(new()
        {
            ["/project/docs/otel/plans"] = new[] { "The-OTEL-Plan.md", "The-OTEL-Plan-2-go-collector.md" },
            ["/project/docs/cross-domain/plans"] = new[] { "The-Cross-Plan.md" },
        });
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain(), new CrossDomain() });
        var builder = new PlansIndexBuilder(resolver, fakeScanner);

        var result = builder.Build("/project", FixedTime);

        Assert.Equal(2, result.PerDomainCounts["otel"]);
        Assert.Equal(1, result.PerDomainCounts["cross-domain"]);
        Assert.Equal(3, result.TotalPlans);
    }

    [Fact(DisplayName = "Plan-9 — total summary line names domain count")]
    public void Total_Summary_Line_Present()
    {
        var fakeScanner = new MultiDirScanner(new());
        var resolver = new DomainResolver(new IDomain[] { new OtelDomain(), new CrossDomain() });
        var builder = new PlansIndexBuilder(resolver, fakeScanner);

        var result = builder.Build("/project", FixedTime);

        Assert.Contains("Total plans across 2 domain(s)", result.Markdown);
    }

    private static PlansIndexBuilder NewBuilder(params IDomain[] domains)
    {
        var resolver = new DomainResolver(domains);
        return new PlansIndexBuilder(resolver, new EmptyScanner());
    }

    private sealed class EmptyScanner : IPlanDirectoryScanner
    {
        public IReadOnlyList<string> ListPlanFileNames(string rootDirectory) => Array.Empty<string>();
    }

    private sealed class MultiDirScanner : IPlanDirectoryScanner
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _byDir;

        public MultiDirScanner(Dictionary<string, IReadOnlyList<string>> byDir)
        {
            // Normalise keys to forward-slashes for cross-platform tests.
            _byDir = byDir.ToDictionary(
                kv => kv.Key.Replace('\\', '/'),
                kv => kv.Value,
                StringComparer.Ordinal);
        }

        public IReadOnlyList<string> ListPlanFileNames(string rootDirectory)
        {
            var key = rootDirectory.Replace('\\', '/');
            return _byDir.TryGetValue(key, out var v) ? v : Array.Empty<string>();
        }
    }
}
