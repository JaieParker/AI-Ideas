namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Settings for the spawned Go OTel collector child process.
/// Bound from the <c>Otel</c> section of appsettings.json.
///
/// Per <c>BR-OTEL-007</c> this class is the **single source of
/// truth** for every collector port. The values flow:
///
///   appsettings.json (or appsettings.Local.json)
///     → CollectorOptions (typed)
///     → CLAUDE_OTEL_*_PORT env vars on the spawned collector
///       (set by ProcessLifecycle.SpawnAsync via ComponentSpec.EnvironmentVariables)
///     → config.yaml's ${env:CLAUDE_OTEL_*_PORT:-default} substitutions
///       (consumed natively by OTel Collector at startup)
///
/// The same options class also drives the sidecar's own
/// consumers: <see cref="CollectorControlClient"/>'s base URLs,
/// dispatch endpoints' error messages, and <c>/demo</c>'s
/// pre-flight port probe — every reference resolves the value
/// from this class, never from an inline literal.
///
/// Defaults here mirror the defaults in
/// <c>appsettings.json</c>; they exist so a fresh test
/// harness without an appsettings file still gets a sensible
/// starting point.
/// </summary>
public sealed class CollectorOptions
{
    public const string SectionName = "Otel";

    public string CollectorExePath { get; set; } =
        System.IO.Path.Combine("dist", "windows-amd64", "claude-otel-collector.exe");

    public string CollectorConfigFile { get; set; } = "config.yaml";

    /// <summary>
    /// Scheme of the collector's HTTP surfaces (control + healthz).
    /// Defaults to <c>http</c>; future deployments that put the
    /// collector behind TLS set this to <c>https</c>. Per
    /// <c>BR-OTEL-001</c>, non-loopback binding requires the
    /// collector binary's own startup-banner override.
    /// </summary>
    public string CollectorScheme { get; set; } = "http";

    /// <summary>
    /// Host the collector binds AND the sidecar reaches it at.
    /// Defaults to <c>127.0.0.1</c>; future container/sidecar-pod
    /// deployments that put the collector on a sister host set
    /// this to that host's address. Both halves move together
    /// (the sidecar's <see cref="CollectorControlClient"/> and
    /// the spawned collector's <c>config.yaml</c> bind targets)
    /// because both consume this single value.
    /// </summary>
    public string CollectorHost { get; set; } = "127.0.0.1";

    public int CollectorOtlpPort { get; set; } = 4318;

    public int CollectorControlPort { get; set; } = 13133;

    public int CollectorHealthzPort { get; set; } = 13134;

    /// <summary>
    /// Scheme of the downstream OTLP exporter target (the
    /// optional <c>otlphttp</c> exporter that forwards enriched
    /// telemetry to a backend like Honeycomb / Tempo / Datadog).
    /// </summary>
    public string DownstreamScheme { get; set; } = "http";

    /// <summary>
    /// Host of the downstream OTLP exporter target. Default is
    /// <c>localhost</c> (same name the upstream sample uses).
    /// </summary>
    public string DownstreamHost { get; set; } = "localhost";

    public int DownstreamOtlpPort { get; set; } = 4319;
}
