using HelpersSidecar.Domain;

namespace HelpersSidecar.Endpoints;

public static class SlugifyEndpoint
{
    public static IEndpointRouteBuilder MapSlugify(this IEndpointRouteBuilder app)
    {
        app.MapPost("/helpers/topics/slugify", (SlugifyRequest req) =>
        {
            var result = TopicSlug.TryCreate(req.Input);
            return result.Ok
                ? Results.Ok(new SlugifyResponse(result.Slug!.Value))
                : Results.BadRequest(new ErrorResponse(result.Error!));
        })
        .WithName("SlugifyTopic")
        .WithSummary("Normalise a free-form topic to a kebab-case slug")
        .WithDescription("BR-EXTEND-005. Lowercases, replaces non-alphanumeric runs " +
            "with single hyphens, trims edge hyphens, truncates to 64 chars, rejects " +
            "input that normalises to an empty string.");

        return app;
    }
}

public sealed record SlugifyRequest(string Input);
public sealed record SlugifyResponse(string Slug);
