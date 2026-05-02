using HelpersSidecar.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Bind 127.0.0.1:5050 by default. BR-OTEL-001 / BR-HELPERS-002.
// Override via Listener:Address / Listener:Port in appsettings or env vars.
var listenerAddress = builder.Configuration["Listener:Address"] ?? "127.0.0.1";
var listenerPort = builder.Configuration.GetValue("Listener:Port", 5050);
builder.WebHost.ConfigureKestrel(o => o.Listen(
    System.Net.IPAddress.Parse(listenerAddress), listenerPort));

// .NET 10 built-in OpenAPI. Generates the spec at /openapi/v1.json.
builder.Services.AddOpenApi("v1");

var app = builder.Build();

// /openapi/v1.json — machine-readable spec. BR-HELPERS-001 says every
// endpoint is reachable through this document; tests verify that gate.
app.MapOpenApi();

var startedAtUtc = DateTimeOffset.UtcNow;
var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

app.MapGet("/healthz", () => Results.Ok(new HealthResponse(
    status: "ok",
    uptime_s: (long)(DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds,
    version: version)))
.WithName("Healthz")
.WithSummary("Liveness probe")
.WithDescription("Returns 200 with status, uptime, and build version when the sidecar is running.");

app.MapSlugify();

app.Run();

internal record HealthResponse(string status, long uptime_s, string version);

// Exposed for WebApplicationFactory in HelpersSidecar.Tests.
public partial class Program;
