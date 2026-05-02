using HelpersSidecar.Domain;

namespace HelpersSidecar.Endpoints;

public static class ValidateEnrichmentEndpoint
{
    public static IEndpointRouteBuilder MapValidateEnrichment(this IEndpointRouteBuilder app)
    {
        app.MapPost("/helpers/enrichments/validate", (ValidateEnrichmentRequest req) =>
        {
            var keyResult = EnrichmentKey.TryCreate(req.Key);
            if (!keyResult.Ok)
                return Results.BadRequest(new ErrorResponse(keyResult.Error!));

            var valueResult = EnrichmentValue.TryCreate(req.Value);
            if (!valueResult.Ok)
                return Results.BadRequest(new ErrorResponse(valueResult.Error!));

            return Results.Ok(new ValidateEnrichmentResponse(
                Key: keyResult.Key!.Value,
                Value: valueResult.Value!.Value,
                Warnings: valueResult.Warnings.ToArray()));
        })
        .WithName("ValidateEnrichment")
        .WithSummary("Validate an enrichment key/value pair")
        .WithDescription("BR-ENRICH-001 (key syntax) and BR-ENRICH-002 (value length) " +
            "fail with HTTP 400. BR-ENRICH-003 (obvious-secret-prefix detection) is " +
            "non-blocking: the response is 200 with the warning attached.");

        return app;
    }
}

public sealed record ValidateEnrichmentRequest(string Key, string Value);
public sealed record ValidateEnrichmentResponse(string Key, string Value, string[] Warnings);
