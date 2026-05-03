using System.Text;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Domain;

/// <summary>
/// Production <see cref="IDemoReportWriter"/>. Renders a markdown
/// report with the BR-DEMO-004 layout. Reuses
/// <see cref="JsonlSliceReader"/> to fetch per-step OTEL records.
/// </summary>
public sealed class MarkdownDemoReportWriter : IDemoReportWriter
{
    public const string SchemaVersion = "DEMO_REPORT v1";
    public const string DefaultReportsDir = "output/demo-reports";
    public const string DefaultTelemetryFile = "output/telemetry.jsonl";

    private readonly JsonlSliceReader _jsonl;
    private readonly string _reportsDir;

    public MarkdownDemoReportWriter(JsonlSliceReader? jsonl = null, string? reportsDir = null)
    {
        _jsonl = jsonl ?? new JsonlSliceReader(DefaultTelemetryFile);
        _reportsDir = reportsDir ?? DefaultReportsDir;
    }

    public async Task<string> WriteAsync(DemoReportInput input, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_reportsDir);

        var ts = input.DemoStartedAt.ToString("yyyyMMddTHHmmssZ");
        var path = Path.Combine(_reportsDir, $"{ts}-{input.DomainName}.md");

        var content = Render(input);
        await File.WriteAllTextAsync(path, content, ct);
        return path;
    }

    private string Render(DemoReportInput input)
    {
        var sb = new StringBuilder();
        var totalElapsed = (input.DemoEndedAt - input.DemoStartedAt).TotalMilliseconds;
        var stepsPass = input.Steps.Count(s => s.Pass);

        // Header
        sb.AppendLine($"# Demo report — {input.DomainName} domain");
        sb.AppendLine();
        sb.AppendLine(SchemaVersion);
        sb.AppendLine();
        sb.AppendLine($"- Generated: {input.DemoStartedAt:O}");
        sb.AppendLine($"- Session id: {input.SessionId}");
        sb.AppendLine($"- Plan tag: {input.PlanTag ?? "(no plan enrichment for this session)"}");
        sb.AppendLine($"- Total elapsed: {totalElapsed:F1} ms");
        sb.AppendLine($"- Pre-flight: {input.PreflightPass}/{input.PreflightTotal} PASS");
        sb.AppendLine($"- Live steps: {stepsPass}/{input.Steps.Count} PASS");
        sb.AppendLine();

        // Pre-flight table
        sb.AppendLine("## Pre-flight");
        sb.AppendLine();
        sb.AppendLine("| step | result | detail |");
        sb.AppendLine("|------|--------|--------|");
        foreach (var p in input.Preflight)
        {
            var status = p.Pass ? "PASS" : "FAIL";
            var detail = p.Fix is null ? p.Detail : $"{p.Detail} — fix: {p.Fix}";
            sb.AppendLine($"| {p.Id} | {status} | {EscapePipe(detail)} |");
        }
        sb.AppendLine();

        // Live demo steps with per-step OTEL records
        sb.AppendLine("## Live demo steps");
        sb.AppendLine();

        var sliceFilter = new JsonlSliceFilter(SessionId: input.SessionId);
        var allSessionRecords = _jsonl.ReadSlice(sliceFilter);
        var assignedRecords = new HashSet<string>();

        foreach (var step in input.Steps)
        {
            var elapsedMs = (step.EndedAt - step.StartedAt).TotalMilliseconds;
            var status = step.Pass ? "PASS" : "FAIL";
            sb.AppendLine($"### STEP {step.Number:00} — {step.Label} ({elapsedMs:F0} ms) — {status}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(step.Detail))
                sb.AppendLine($"- {step.Detail}");

            // Per-step JSONL records: timestamps inside [StartedAt, EndedAt].
            var stepRecords = step.StartedAt == default || step.EndedAt == default
                ? Array.Empty<JsonlRecord>()
                : allSessionRecords
                    .Where(r => r.Timestamp is not null
                                && r.Timestamp.Value >= step.StartedAt
                                && r.Timestamp.Value <= step.EndedAt)
                    .ToArray();
            foreach (var r in stepRecords) assignedRecords.Add(r.RawLine);

            if (stepRecords.Length == 0)
            {
                sb.AppendLine($"- OTEL records produced: 0");
            }
            else
            {
                sb.AppendLine($"- OTEL records produced: {stepRecords.Length}");
                sb.AppendLine();
                sb.AppendLine("```jsonl");
                foreach (var r in stepRecords)
                    sb.AppendLine(r.RawLine);
                sb.AppendLine("```");
            }
            sb.AppendLine();
        }

        // Final summary
        sb.AppendLine("## Final summary");
        sb.AppendLine();
        sb.AppendLine($"DEMO RESULT: {stepsPass}/{input.Steps.Count} PASS");
        sb.AppendLine();

        // Teardown
        sb.AppendLine("## Teardown");
        sb.AppendLine();
        sb.AppendLine(input.TeardownText);
        sb.AppendLine();

        // Appendix: records that didn't fall into any step's window
        var orphans = allSessionRecords
            .Where(r => !assignedRecords.Contains(r.RawLine))
            .ToArray();
        sb.AppendLine($"## Appendix — full session JSONL slice ({allSessionRecords.Count} records, {orphans.Length} not assigned to any step)");
        sb.AppendLine();
        if (allSessionRecords.Count == 0)
        {
            sb.AppendLine("(no JSONL records matched this session — collector may not be running, or the session.id attribute hasn't propagated)");
        }
        else
        {
            sb.AppendLine("```jsonl");
            foreach (var r in allSessionRecords)
                sb.AppendLine(r.RawLine);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static string EscapePipe(string s) => s.Replace("|", @"\|");
}
