using HelpersSidecar.Application;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

public static class NextPlanNameEndpoint
{
    public static IEndpointRouteBuilder MapNextPlanName(this IEndpointRouteBuilder app)
    {
        app.MapPost("/helpers/plans/next-name",
            (NextPlanNameRequest req, IPlanDirectoryScanner scanner, IDomainResolver domains) =>
        {
            string? slug = null;
            if (!string.IsNullOrEmpty(req.Slug))
            {
                var slugResult = TopicSlug.TryCreate(req.Slug);
                if (!slugResult.Ok)
                    return Results.BadRequest(new ErrorResponse(slugResult.Error!));
                slug = slugResult.Slug!.Value;
            }

            // Phase 2b — Domain is optional on the request; defaults
            // to "otel" for backward compat. Phase 2c will tighten
            // when the rename + explicit domain wiring lands.
            var domainName = string.IsNullOrEmpty(req.Domain) ? "otel" : req.Domain;
            if (!domains.TryResolve(domainName, out var domain))
                return Results.BadRequest(new ErrorResponse(
                    $"unknown domain '{domainName}' (known: {string.Join(", ", domains.KnownNames)})"));

            // Plan-9: when Root is omitted, default to the resolved
            // domain's PlanFiles.Directory. An explicit Root in the
            // request still wins (tests rely on this for cwd-free
            // scanning). Directory == "." → cwd to preserve back-compat.
            var root = string.IsNullOrEmpty(req.Root)
                ? (domain!.PlanFiles.Directory == "." ? "." : domain!.PlanFiles.Directory)
                : req.Root;
            var existing = scanner.ListPlanFileNames(root);
            var next = NextPlanFileName.Compute(existing, domain!.PlanFiles, slug);

            return Results.Ok(new NextPlanNameResponse(next.FileName, next.Number));
        })
        .WithName("NextPlanName")
        .WithSummary("Compute the next available plan filename for a domain")
        .WithDescription("BR-EXTEND-004 + BR-EXTEND-006. Scans <Root> for plan files matching " +
            "the resolved domain's PlanFileConventions, returns the next-N filename " +
            "(or the base file if no plans exist). Slug is normalised through TopicSlug. " +
            "Domain defaults to 'otel' if omitted; an invalid slug or unknown domain returns 400.");

        return app;
    }
}

public sealed record NextPlanNameRequest(string Root, string? Slug = null, string? Domain = null);
public sealed record NextPlanNameResponse(string Name, int Number);
