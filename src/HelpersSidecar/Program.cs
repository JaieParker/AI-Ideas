using HelpersSidecar.Application;
using HelpersSidecar.Domain;
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

// Load appsettings.json + appsettings.{Environment}.json from the
// binary's directory (AppContext.BaseDirectory), not just from the
// content root. The sidecar is invoked as `dotnet HelpersSidecar.dll`
// from the project root, where the working directory has no
// appsettings — those files live next to the DLL. Without this,
// appsettings.Development.json's CollectorOtlpPort=14318 override
// is silently dropped on dev machines that re-port the collector.
// Working files (output/, persistent-enrichments.json) stay
// content-root-relative; only configuration files come from the
// binary's directory.
var binDir = AppContext.BaseDirectory;
builder.Configuration
    .AddJsonFile(Path.Combine(binDir, "appsettings.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(binDir, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: false);

builder.Services.AddSingleton<IPlanDirectoryScanner, PlanDirectoryScanner>();
builder.Services.AddSingleton<ICollectorControlClient, CollectorControlClient>();
builder.Services.AddSingleton<IPortProbe, PortProbe>();
builder.Services.AddSingleton<IBuildRunner, BuildRunner>();
builder.Services.AddSingleton<IHealthChecker, HttpHealthChecker>();
builder.Services.AddSingleton<IComponentRegistry>(sp =>
    ComponentRegistry.Default(
        sidecarPort: builder.Configuration.GetValue("Listener:Port", 5050),
        sidecarExe: Path.Combine("src", "HelpersSidecar", "bin", "Debug", "net10.0", "HelpersSidecar.dll"),
        runtimeDir: LifecycleCli.RuntimeDir,
        collectorExe: builder.Configuration.GetValue<string?>("Otel:CollectorExePath",
            Path.Combine("dist", "windows-amd64", "claude-otel-collector.exe")),
        collectorConfigFile: builder.Configuration.GetValue<string?>("Otel:CollectorConfigFile", "config.yaml"),
        sidecarStagingPort: builder.Configuration.GetValue<int?>("Lifecycle:Staging:SidecarPort", 5051)));
builder.Services.AddSingleton<ProcessLifecycle>();
builder.Services.AddSingleton<IProcessLifecycle>(sp => sp.GetRequiredService<ProcessLifecycle>());
builder.Services.AddSingleton<IStageableLifecycle>(sp => sp.GetRequiredService<ProcessLifecycle>());

builder.Services.Configure<SkillDispatchOptions>(
    builder.Configuration.GetSection(SkillDispatchOptions.SectionName));
builder.Services.AddHttpClient<ISkillDispatchClient, SkillDispatchClient>();

// BR-EXTEND-006 — register every IDomain implementation as a
// singleton. IDomainResolver wraps them all and exposes name-based
// lookup. Adding a new domain is one new IDomain class + one
// AddSingleton line — no consumer changes.
builder.Services.AddSingleton<IDomain, OtelDomain>();
// Plan-9: cross-domain is a first-class virtual domain reserving
// docs/cross-domain/plans/ for plans that span domains. No skill,
// empty GovernedGlobs — purely a knowledge-facade for the multi-
// directory scanner and the index endpoint.
builder.Services.AddSingleton<IDomain, CrossDomain>();
builder.Services.AddSingleton<IDomainResolver, DomainResolver>();

// BR-EXTEND-010 — IDomainDemo is the optional companion contract.
// A domain with a demo registers one; absence is fine ("no demo
// for this domain" rendered by the dispatch endpoint).
builder.Services.AddSingleton<IDomainDemo, OtelDomainDemo>();

// BR-DEMO-004 — durable demo reports written to output/demo-reports/.
builder.Services.AddSingleton<IDemoReportWriter>(sp => new MarkdownDemoReportWriter());

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
app.MapPlansIndex();
app.MapIntegrationTestScope();
app.MapArchitectureReviewGate();
app.MapWeatherDispatch();
app.MapEnrichDispatch();
app.MapOtelDispatch();
app.MapExtendSkillsDispatch();
app.MapDemoDispatch();
app.MapDomainInfoDispatch();
app.MapArchitectureReviewDispatch();

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
