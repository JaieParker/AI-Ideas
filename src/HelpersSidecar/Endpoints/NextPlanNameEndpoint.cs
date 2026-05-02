using HelpersSidecar.Application;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

public static class NextPlanNameEndpoint
{
    public static IEndpointRouteBuilder MapNextPlanName(this IEndpointRouteBuilder app)
    {
        app.MapPost("/helpers/plans/next-name",
            (NextPlanNameRequest req, IPlanDirectoryScanner scanner) =>
        {
            string? slug = null;
            if (!string.IsNullOrEmpty(req.Slug))
            {
                var slugResult = TopicSlug.TryCreate(req.Slug);
                if (!slugResult.Ok)
                    return Results.BadRequest(new ErrorResponse(slugResult.Error!));
                slug = slugResult.Slug!.Value;
            }

            var existing = scanner.ListPlanFileNames(req.Root);
            var next = NextPlanFileName.Compute(existing, slug);

            return Results.Ok(new NextPlanNameResponse(next.FileName, next.Number));
        })
        .WithName("NextPlanName")
        .WithSummary("Compute the next available plan filename")
        .WithDescription("BR-EXTEND-004. Scans <root> for The-OTEL-Plan*.md files, " +
            "returns The-OTEL-Plan-<max+1>(-<slug>)?.md (or the base file if no plans " +
            "exist). Slug is normalised through TopicSlug; an invalid slug returns 400.");

        return app;
    }
}

public sealed record NextPlanNameRequest(string Root, string? Slug = null);
public sealed record NextPlanNameResponse(string Name, int Number);
