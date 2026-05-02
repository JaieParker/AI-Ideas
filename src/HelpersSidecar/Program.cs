using HelpersSidecar.Application;
using HelpersSidecar.Endpoints;
using HelpersSidecar.Infrastructure;

// CLI mode: --lifecycle <verb> <component> runs the lifecycle CLI
// without starting Kestrel (BR-PROCESS-008). Used by /skill-bootstrap
// before the sidecar is up — chicken-and-egg solved by short-circuit.
if (args.Length > 0 && args[0] == LifecycleCli.Flag)
{
    return await LifecycleCli.RunAsync(args.Skip(1).ToArray());
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPlanDirectoryScanner, PlanDirectoryScanner>();
builder.Services.AddSingleton<ICollectorControlClient, CollectorControlClient>();
builder.Services.AddSingleton<IPortProbe, PortProbe>();
builder.Services.AddSingleton<IComponentRegistry>(sp =>
    ComponentRegistry.Default(
        sidecarPort: builder.Configuration.GetValue("Listener:Port", 5050),
        sidecarExe: Path.Combine("src", "HelpersSidecar", "bin", "Debug", "net10.0", "HelpersSidecar.dll"),
        runtimeDir: LifecycleCli.RuntimeDir,
        collectorExe: builder.Configuration.GetValue<string?>("Otel:CollectorExePath",
            Path.Combine("dist", "windows-amd64", "claude-otel-collector.exe")),
        collectorConfigFile: builder.Configuration.GetValue<string?>("Otel:CollectorConfigFile", "config.yaml")));
builder.Services.AddSingleton<IProcessLifecycle, ProcessLifecycle>();

builder.Services.Configure<SkillDispatchOptions>(
    builder.Configuration.GetSection(SkillDispatchOptions.SectionName));
builder.Services.AddHttpClient<ISkillDispatchClient, SkillDispatchClient>();

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
app.MapValidateEnrichment();
app.MapNextPlanName();
app.MapWeatherDispatch();
app.MapEnrichDispatch();
app.MapOtelDispatch();
app.MapOtelExtendDispatch();
app.MapDemoDispatch();

// Write our PID file at startup ONLY when running under real Kestrel
// (not WebApplicationFactory's TestServer); remove on graceful shutdown.
// Detected by /skill-bootstrap via --lifecycle probe sidecar
// (BR-PROCESS-008). Forced kills may leave the file behind; the next
// /skill-bootstrap start sweeps it.
var pidPath = Path.Combine(LifecycleCli.RuntimeDir, "sidecar.pid");
app.Lifetime.ApplicationStarted.Register(() =>
{
    var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
    if (server.GetType().FullName == "Microsoft.AspNetCore.TestHost.TestServer") return;

    try
    {
        Directory.CreateDirectory(LifecycleCli.RuntimeDir);
        File.WriteAllText(pidPath, Environment.ProcessId.ToString());
    }
    catch { /* tolerate; not fatal — sweep on next bootstrap */ }
});
app.Lifetime.ApplicationStopping.Register(() =>
{
    try { if (File.Exists(pidPath)) File.Delete(pidPath); }
    catch { /* tolerate */ }
});

app.Run();
return 0;

internal record HealthResponse(string status, long uptime_s, string version);

// Exposed for WebApplicationFactory in HelpersSidecar.Tests.
public partial class Program;
