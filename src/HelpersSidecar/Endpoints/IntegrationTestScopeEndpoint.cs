using HelpersSidecar.Application;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// /helpers/integration-test-scope — BR-EXTEND-011 consumer.
/// Takes a list of changed file paths (project-root-relative,
/// typically from <c>git diff --name-only</c>) and returns the
/// set of impacted domains. CI scripts and pre-commit hooks call
/// this to scope integration-test runs.
///
/// <para>Pure pass-through to <see cref="DomainImpactScope"/>;
/// the endpoint exists so non-.NET callers (a bash CI script,
/// a git hook, a future GitHub Action) can talk to the same
/// resolver without re-implementing the matching logic.</para>
/// </summary>
public static class IntegrationTestScopeEndpoint
{
    public static IEndpointRouteBuilder MapIntegrationTestScope(this IEndpointRouteBuilder app)
    {
        app.MapPost("/helpers/integration-test-scope",
            (IntegrationTestScopeRequest req, IDomainResolver domains) =>
            {
                if (req.ChangedPaths is null)
                    return Results.BadRequest(new { error = "ChangedPaths is required (may be empty array)" });

                var scope = new DomainImpactScope(domains);
                var result = scope.ResolveImpactedDomains(req.ChangedPaths);

                return Results.Ok(new IntegrationTestScopeResponse(
                    ImpactedDomains: result.ImpactedDomains,
                    CrossDomainTriggered: result.CrossDomainTriggered,
                    TestFilterHint: BuildTestFilterHint(result.ImpactedDomains)));
            })
            .WithName("IntegrationTestScope")
            .WithSummary("BR-EXTEND-011 — domain-scoped integration test resolution")
            .WithDescription("POST a list of changed file paths; receive the set of " +
                "impacted domain names plus a TestFilterHint suitable for " +
                "`dotnet test --filter`. Cross-domain changes return every domain. " +
                "An empty change list returns an empty impact set.");

        return app;
    }

    private static string BuildTestFilterHint(IReadOnlyList<string> impactedDomains)
    {
        if (impactedDomains.Count == 0)
            return string.Empty;

        // dotnet test --filter syntax: combine per-domain filters with |.
        // Domain name appears in test display names (e.g. "BR-EXTEND-011 — …")
        // but not as a structured property, so we hint at FullyQualifiedName
        // matching the IDomain class name pattern.
        var clauses = impactedDomains
            .Select(d => $"FullyQualifiedName~{ToPascalCase(d)}");
        return string.Join("|", clauses);
    }

    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}

public sealed record IntegrationTestScopeRequest(IReadOnlyList<string> ChangedPaths);

public sealed record IntegrationTestScopeResponse(
    IReadOnlyList<string> ImpactedDomains,
    bool CrossDomainTriggered,
    string TestFilterHint);
