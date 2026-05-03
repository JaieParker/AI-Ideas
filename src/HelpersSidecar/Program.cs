using HelpersSidecar.Application;
using HelpersSidecar.Artefacts;
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

// Load appsettings.json + appsettings.{Environment}.json +
// appsettings.Local.json from the binary's directory
// (AppContext.BaseDirectory), not just from the content root. The
// sidecar is invoked as `dotnet HelpersSidecar.dll` from the project
// root, where the working directory has no appsettings — those files
// live next to the DLL. Without this, dev/local overrides are
// silently dropped (BR-CODE-002). Working files (output/,
// persistent-enrichments.json) stay content-root-relative; only
// configuration files come from the binary's directory.
//
// appsettings.Local.json is the **single local override file** for
// per-machine settings (BR-OTEL-007). Gitignored. Loaded last so it
// wins over appsettings.json and appsettings.{Env}.json. Today the
// only setting users typically override here is Otel:CollectorOtlpPort
// (when :4318 is held locally and the collector needs a different
// port).
var binDir = AppContext.BaseDirectory;
builder.Configuration
    .AddJsonFile(Path.Combine(binDir, "appsettings.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(binDir, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(binDir, "appsettings.Local.json"), optional: true, reloadOnChange: false);

builder.Services.AddSingleton<IPlanDirectoryScanner, PlanDirectoryScanner>();
builder.Services.AddSingleton<ICollectorControlClient, CollectorControlClient>();
builder.Services.AddSingleton<IPortProbe, PortProbe>();
builder.Services.AddSingleton<IBuildRunner, BuildRunner>();
builder.Services.AddSingleton<IHealthChecker, HttpHealthChecker>();
builder.Services.AddSingleton<ISkillRewriter, SkillRewriter>();
// BR-OTEL-007 — every collector port has a single source of truth in
// CollectorOptions, bound from the Otel section. Defaults in the class
// initialisers mirror appsettings.json so a fresh test harness without
// the JSON file still gets sensible values.
builder.Services.Configure<CollectorOptions>(
    builder.Configuration.GetSection(CollectorOptions.SectionName));

// BR-HELPERS-002 / BR-SKILL-015 — the sidecar's own deployment shape
// (direct host process vs container) is a typed setting; mode switches
// are skill-driven (/skill-bootstrap set-mode) and gated on HITL
// confirmation per the amended BR-HELPERS-002.
builder.Services.Configure<SidecarOptions>(
    builder.Configuration.GetSection(SidecarOptions.SectionName));

builder.Services.AddSingleton<IComponentRegistry>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CollectorOptions>>().Value;
    return ComponentRegistry.Default(
        sidecarPort: builder.Configuration.GetValue("Listener:Port", 5050),
        sidecarExe: Path.Combine("src", "HelpersSidecar", "bin", "Debug", "net10.0", "HelpersSidecar.dll"),
        runtimeDir: LifecycleCli.RuntimeDir,
        collectorExe: opts.CollectorExePath,
        collectorConfigFile: opts.CollectorConfigFile,
        sidecarStagingPort: builder.Configuration.GetValue<int?>("Lifecycle:Staging:SidecarPort", 5051),
        collectorOptions: opts);
});
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
// Plan-11: routes through IArtefactWriter when DI provides one.
builder.Services.AddSingleton<IDemoReportWriter>(sp => new MarkdownDemoReportWriter(
    artefacts: sp.GetRequiredService<IArtefactWriter>()));

// BR-SKILL-013 — /ai-level scoring against the 4 D rubric.
// Plan-10 wires the deterministic half (checker + report writer);
// the in-session Claude scores the judgement half (per BR-SKILL-012).
builder.Services.AddSingleton<AiLevelChecker>();
builder.Services.AddSingleton<AiLevelReportWriter>();

// BR-PROCESS-015 — every durable artefact registered. Plan-11
// ships the catalogue; producers retrofit through IArtefactWriter.
// BR-SECURITY-004 — only LocalFileDestination shipped today;
// remote destinations (S3, database, webhook) require explicit
// opt-in machinery before any future plan adds them.
foreach (var spec in ArtefactSpecs.All)
    builder.Services.AddSingleton(spec);
builder.Services.AddSingleton<IArtefactRegistry, ArtefactRegistry>();
builder.Services.AddSingleton<IArtefactDestination, LocalFileDestination>();
builder.Services.AddSingleton<IArtefactWriter, ArtefactWriter>();

// Bind 127.0.0.1:5050 by default. BR-OTEL-001 / BR-HELPERS-002.
// Override via Listener:Address / Listener:Port in appsettings or env vars.
var listenerAddress = builder.Configuration["Listener:Address"] ?? "127.0.0.1";
var listenerPort = builder.Configuration.GetValue("Listener:Port", 5050);
builder.WebHost.ConfigureKestrel(o => o.Listen(
    System.Net.IPAddress.Parse(listenerAddress), listenerPort));

// BR-HELPERS-002 (amended) — when the sidecar binds anything other
// than the loopback default, write a one-line banner so the
// operator can see what's been chosen. The amended rule recognises
// 0.0.0.0 inside a container as a deliberate, port-mapped
// deployment shape — not a violation — provided the host's loopback
// contract is preserved by `docker run -p 127.0.0.1:5050:5050 ...`.
if (listenerAddress != "127.0.0.1")
{
    var sidecarMode = builder.Configuration.GetValue<string>("Sidecar:Mode") ?? "Direct";
    var shape = sidecarMode.Equals("Container", StringComparison.OrdinalIgnoreCase)
        ? "container deployment — host loopback contract preserved via port mapping"
        : "non-loopback bind — operator override (BR-HELPERS-002)";
    Console.Error.WriteLine($"Sidecar bound {listenerAddress}:{listenerPort} ({shape})");
}

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
app.MapAiLevelDispatch();
app.MapSkillRewriteDispatch();

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
