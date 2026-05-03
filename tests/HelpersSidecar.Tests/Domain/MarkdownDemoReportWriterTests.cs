using System.Text.RegularExpressions;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Domain;

/// <summary>
/// BR-DEMO-004 / BR-PROCESS-013 — MarkdownDemoReportWriter renders
/// a durable lifecycle report at output/demo-reports/&lt;ts&gt;-&lt;domain&gt;.md.
/// Per BR-PROCESS-007 tests scope to the writer's seam: a custom
/// JsonlSliceReader returns canned records; assertions check the
/// markdown layout shape, not the production telemetry.jsonl.
/// </summary>
public class MarkdownDemoReportWriterTests : IDisposable
{
    private readonly string _reportsDir;

    public MarkdownDemoReportWriterTests()
    {
        // Each test gets its own reports dir; injected into the
        // writer (no cwd swap — that breaks parallel isolation).
        _reportsDir = Path.Combine(Path.GetTempPath(), $"demo-report-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_reportsDir, recursive: true); } catch { /* best-effort */ }
    }

    private MarkdownDemoReportWriter NewWriter() =>
        new(new JsonlSliceReader(Path.Combine(_reportsDir, "no-jsonl-here")), _reportsDir);

    [Fact(DisplayName = "BR-DEMO-004 — report file lands at output/demo-reports/<ts>-<domain>.md")]
    public async Task Report_Path_Has_Correct_Shape()
    {
        var input = MakeInput(domain: "otel", startedAt: DateTimeOffset.Parse("2026-05-03T10:30:45Z"));
        var writer = NewWriter();

        var path = await writer.WriteAsync(input);

        Assert.EndsWith($"20260503T103045Z-otel.md", path.Replace('\\', '/'));
        Assert.True(File.Exists(path));
    }

    [Fact(DisplayName = "BR-DEMO-004 — report header has schema version, timestamp, session id, plan tag")]
    public async Task Header_Has_Required_Fields()
    {
        var input = MakeInput(planTag: "The-OTEL-Plan-N-test.md");
        var writer = NewWriter();

        var path = await writer.WriteAsync(input);
        var body = await File.ReadAllTextAsync(path);

        Assert.Contains("DEMO_REPORT v1", body);
        Assert.Contains("Session id:", body);
        Assert.Contains("Plan tag: The-OTEL-Plan-N-test.md", body);
        Assert.Contains("Total elapsed:", body);
        Assert.Contains("Pre-flight:", body);
        Assert.Contains("Live steps:", body);
    }

    [Fact(DisplayName = "BR-DEMO-004 — pre-flight rows render in a markdown table")]
    public async Task Preflight_Renders_As_Table()
    {
        var input = MakeInput(preflight: new[]
        {
            new DemoPreflightRow("00.a", true, "row a passed", null),
            new DemoPreflightRow("00.b", false, "row b failed", "do this thing"),
        });
        var writer = NewWriter();

        var path = await writer.WriteAsync(input);
        var body = await File.ReadAllTextAsync(path);

        Assert.Contains("| step | result | detail |", body);
        Assert.Contains("| 00.a | PASS |", body);
        Assert.Contains("| 00.b | FAIL |", body);
        Assert.Contains("fix: do this thing", body);
    }

    [Fact(DisplayName = "BR-DEMO-004 — every step gets a section with elapsed + status + detail")]
    public async Task Each_Step_Has_Section()
    {
        var startedAt = DateTimeOffset.Parse("2026-05-03T10:00:00Z");
        var input = MakeInput(steps: new[]
        {
            new DemoStepResult(1, "label one", true, "detail one", startedAt, startedAt.AddMilliseconds(50)),
            new DemoStepResult(2, "label two", false, "detail two", startedAt.AddMilliseconds(100), startedAt.AddMilliseconds(200)),
        });
        var writer = NewWriter();

        var path = await writer.WriteAsync(input);
        var body = await File.ReadAllTextAsync(path);

        Assert.Contains("### STEP 01 — label one (50 ms) — PASS", body);
        Assert.Contains("- detail one", body);
        Assert.Contains("### STEP 02 — label two (100 ms) — FAIL", body);
    }

    [Fact(DisplayName = "BR-DEMO-004 — final summary line is parseable as DEMO RESULT: x/y PASS")]
    public async Task Summary_Is_Parseable()
    {
        var input = MakeInput(steps: new[]
        {
            new DemoStepResult(1, "a", true, "x"),
            new DemoStepResult(2, "b", false, "y"),
            new DemoStepResult(3, "c", true, "z"),
        });
        var writer = NewWriter();

        var path = await writer.WriteAsync(input);
        var body = await File.ReadAllTextAsync(path);

        Assert.Matches(new Regex(@"DEMO RESULT: 2/3 PASS"), body);
    }

    [Fact(DisplayName = "BR-DEMO-004 — appendix names total session records + count of orphans")]
    public async Task Appendix_Counts_Records()
    {
        var input = MakeInput();
        var writer = NewWriter();

        var path = await writer.WriteAsync(input);
        var body = await File.ReadAllTextAsync(path);

        Assert.Contains("## Appendix", body);
        Assert.Contains("0 records, 0 not assigned", body);
    }

    [Fact(DisplayName = "BR-DEMO-004 — teardown text from input is rendered into the Teardown section")]
    public async Task Teardown_Text_Rendered()
    {
        var input = MakeInput(teardownText: "Custom teardown instructions here.");
        var writer = NewWriter();

        var path = await writer.WriteAsync(input);
        var body = await File.ReadAllTextAsync(path);

        Assert.Contains("## Teardown", body);
        Assert.Contains("Custom teardown instructions here.", body);
    }

    private static DemoReportInput MakeInput(
        string domain = "otel",
        string sessionId = "test-session",
        string? planTag = null,
        IReadOnlyList<DemoPreflightRow>? preflight = null,
        IReadOnlyList<DemoStepResult>? steps = null,
        DateTimeOffset? startedAt = null,
        string teardownText = "Standard teardown.")
    {
        var s = startedAt ?? DateTimeOffset.UtcNow;
        var pre = preflight ?? new[]
        {
            new DemoPreflightRow("00.a", true, "ok", null),
        };
        var st = steps ?? Array.Empty<DemoStepResult>();
        return new DemoReportInput(
            DomainName: domain,
            SessionId: sessionId,
            PlanTag: planTag,
            Preflight: pre,
            PreflightPass: pre.Count(p => p.Pass),
            PreflightTotal: pre.Count,
            Steps: st,
            DemoStartedAt: s,
            DemoEndedAt: s.AddMilliseconds(500),
            TeardownText: teardownText);
    }
}
