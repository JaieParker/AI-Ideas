using System.Text;
using HelpersSidecar.Application;
using HelpersSidecar.Artefacts;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// /helpers/plans/index — composes a cross-domain markdown index
/// of every registered domain's plan files (Plan-9 cross-domain
/// discoverability promise). Output is schema-versioned
/// (<c>PLAN_INDEX v1</c>) per <c>BR-PROCESS-013</c>.
///
/// <para>Two modes via the optional <c>WriteTo</c> field:</para>
/// <list type="bullet">
///   <item>Empty / null → returns the markdown body in the response.
///     Pure-read, no side effects. Good for ad-hoc inspection.</item>
///   <item>A path (absolute or project-root-relative) → writes the
///     markdown to that path AND returns it in the response.
///     Caller's responsibility to commit the resulting file.</item>
/// </list>
/// </summary>
public static class PlansIndexEndpoint
{
    public static IEndpointRouteBuilder MapPlansIndex(this IEndpointRouteBuilder app)
    {
        app.MapPost("/helpers/plans/index",
            async (PlansIndexRequest? req, IDomainResolver domains, IPlanDirectoryScanner scanner, IArtefactWriter artefacts) =>
            {
                req ??= new PlansIndexRequest();
                var projectRoot = string.IsNullOrEmpty(req.ProjectRoot)
                    ? Directory.GetCurrentDirectory()
                    : req.ProjectRoot;

                var builder = new PlansIndexBuilder(domains, scanner);
                var result = builder.Build(projectRoot, DateTimeOffset.UtcNow);

                string? writtenTo = null;
                if (!string.IsNullOrEmpty(req.WriteTo))
                {
                    // Plan-11 retrofit: route through IArtefactWriter when the
                    // canonical "plans-index" key (docs/INDEX.md) is requested.
                    // An override path (req.WriteTo not equal to docs/INDEX.md)
                    // still works by falling back to direct File.WriteAllText —
                    // the registry doesn't model arbitrary destinations yet.
                    if (req.WriteTo == "docs/INDEX.md")
                    {
                        var body = Encoding.UTF8.GetBytes(result.Markdown).AsMemory();
                        var write = await artefacts.WriteAsync("plans-index",
                            new Dictionary<string, string>(), body);
                        writtenTo = Path.IsPathRooted(write.ResolvedKey)
                            ? write.ResolvedKey
                            : Path.Combine(projectRoot, write.ResolvedKey);
                    }
                    else
                    {
                        var target = Path.IsPathRooted(req.WriteTo)
                            ? req.WriteTo
                            : Path.Combine(projectRoot, req.WriteTo);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        await File.WriteAllTextAsync(target, result.Markdown);
                        writtenTo = target;
                    }
                }

                return Results.Ok(new PlansIndexResponse(
                    Markdown: result.Markdown,
                    PerDomainCounts: result.PerDomainCounts,
                    TotalPlans: result.TotalPlans,
                    WrittenTo: writtenTo));
            })
            .WithName("PlansIndex")
            .WithSummary("Cross-domain plans index (PLAN_INDEX v1)")
            .WithDescription("Walks every registered IDomain's PlanFiles.Directory plus " +
                "the cross-domain virtual domain. Returns the rendered markdown body, the " +
                "per-domain plan counts, and the total. When WriteTo is provided, also " +
                "writes the markdown to that path. Plan-9 cross-domain discoverability.");

        return app;
    }
}

public sealed record PlansIndexRequest(string? ProjectRoot = null, string? WriteTo = null);

public sealed record PlansIndexResponse(
    string Markdown,
    IReadOnlyDictionary<string, int> PerDomainCounts,
    int TotalPlans,
    string? WrittenTo);
