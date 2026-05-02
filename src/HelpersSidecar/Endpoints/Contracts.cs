namespace HelpersSidecar.Endpoints;

/// <summary>Standard error envelope returned by every endpoint that 400s.</summary>
public sealed record ErrorResponse(string Error);
