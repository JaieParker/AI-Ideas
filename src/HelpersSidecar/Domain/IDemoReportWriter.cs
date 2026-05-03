using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Domain;

/// <summary>
/// Writes a durable demo report at
/// <c>output/demo-reports/&lt;UTC-timestamp&gt;-&lt;domain&gt;.md</c>
/// per BR-DEMO-004. The report correlates each step with the OTEL
/// records emitted during its window.
///
/// Per BR-PROCESS-013 the report follows the lifecycle-event-report
/// shape: header (timestamp, target, schema version), per-step
/// section, summary, appendix.
/// </summary>
public interface IDemoReportWriter
{
    /// <summary>
    /// Write the report and return the path. The path is included
    /// in the dispatch endpoint's response so the user knows where
    /// to find it.
    /// </summary>
    Task<string> WriteAsync(DemoReportInput input, CancellationToken ct = default);
}

/// <summary>
/// Inputs the writer needs to render a report.
/// </summary>
public sealed record DemoReportInput(
    string DomainName,
    string SessionId,
    string? PlanTag,
    IReadOnlyList<DemoPreflightRow> Preflight,
    int PreflightPass,
    int PreflightTotal,
    IReadOnlyList<DemoStepResult> Steps,
    DateTimeOffset DemoStartedAt,
    DateTimeOffset DemoEndedAt,
    string TeardownText);

public sealed record DemoPreflightRow(string Id, bool Pass, string Detail, string? Fix);
