using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Infrastructure;

/// <summary>
/// BR-DEMO-004 / BR-DEMO-003 — JsonlSliceReader reads
/// <c>output/telemetry.jsonl</c>, filters by timestamp window +
/// session id + plan enrichment, returns a slice for the demo
/// report writer to embed. File-share semantics survive
/// concurrent collector writes.
/// </summary>
public class JsonlSliceReaderTests : IDisposable
{
    private readonly string _tempFile;

    public JsonlSliceReaderTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"jsonl-test-{Guid.NewGuid():N}.jsonl");
    }

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { /* best-effort */ }
    }

    [Fact(DisplayName = "BR-DEMO-004 — missing file returns empty slice (not throw)")]
    public void Missing_File_Returns_Empty()
    {
        var reader = new JsonlSliceReader(_tempFile);
        var slice = reader.ReadSlice(new JsonlSliceFilter());
        Assert.Empty(slice);
    }

    [Fact(DisplayName = "BR-DEMO-004 — every line in the file is parsed when no filter is set")]
    public void All_Records_Returned_When_Filter_Empty()
    {
        File.WriteAllLines(_tempFile, new[]
        {
            JsonRecord("session-1", "plan-foo", "2026-05-03T10:00:00Z"),
            JsonRecord("session-2", "plan-bar", "2026-05-03T10:00:01Z"),
        });
        var reader = new JsonlSliceReader(_tempFile);
        var slice = reader.ReadSlice(new JsonlSliceFilter());
        Assert.Equal(2, slice.Count);
    }

    [Fact(DisplayName = "BR-DEMO-004 — session-id filter keeps matching records, drops non-matching")]
    public void SessionId_Filter()
    {
        File.WriteAllLines(_tempFile, new[]
        {
            JsonRecord("session-1", "plan-foo", "2026-05-03T10:00:00Z"),
            JsonRecord("session-2", "plan-foo", "2026-05-03T10:00:01Z"),
        });
        var reader = new JsonlSliceReader(_tempFile);
        var slice = reader.ReadSlice(new JsonlSliceFilter(SessionId: "session-1"));
        Assert.Single(slice);
        Assert.Equal("session-1", slice[0].SessionId);
    }

    [Fact(DisplayName = "BR-DEMO-004 — plan-tag filter keeps matching records")]
    public void PlanTag_Filter()
    {
        File.WriteAllLines(_tempFile, new[]
        {
            JsonRecord("s", "plan-foo", "2026-05-03T10:00:00Z"),
            JsonRecord("s", "plan-bar", "2026-05-03T10:00:01Z"),
        });
        var reader = new JsonlSliceReader(_tempFile);
        var slice = reader.ReadSlice(new JsonlSliceFilter(PlanTag: "plan-foo"));
        Assert.Single(slice);
        Assert.Equal("plan-foo", slice[0].PlanTag);
    }

    [Fact(DisplayName = "BR-DEMO-004 — window filter keeps records inside [StartedAt, EndedAt] (inclusive)")]
    public void Window_Filter()
    {
        File.WriteAllLines(_tempFile, new[]
        {
            JsonRecord("s", null, "2026-05-03T10:00:00Z"),  // before window
            JsonRecord("s", null, "2026-05-03T10:00:30Z"),  // inside
            JsonRecord("s", null, "2026-05-03T10:00:45Z"),  // inside
            JsonRecord("s", null, "2026-05-03T10:01:00Z"),  // after window
        });
        var reader = new JsonlSliceReader(_tempFile);
        var slice = reader.ReadSlice(new JsonlSliceFilter(
            StartedAt: DateTimeOffset.Parse("2026-05-03T10:00:15Z"),
            EndedAt:   DateTimeOffset.Parse("2026-05-03T10:00:50Z")));
        Assert.Equal(2, slice.Count);
    }

    [Fact(DisplayName = "BR-DEMO-004 — records without timestamp are excluded when a window is set")]
    public void No_Timestamp_Excluded_When_Window_Set()
    {
        File.WriteAllLines(_tempFile, new[]
        {
            "{\"some\":\"record-without-recognised-timestamp\"}",
        });
        var reader = new JsonlSliceReader(_tempFile);
        var slice = reader.ReadSlice(new JsonlSliceFilter(
            StartedAt: DateTimeOffset.Parse("2026-05-03T10:00:00Z")));
        Assert.Empty(slice);
    }

    [Fact(DisplayName = "BR-DEMO-004 — malformed JSONL line is skipped, not fatal")]
    public void Malformed_Line_Skipped()
    {
        File.WriteAllLines(_tempFile, new[]
        {
            "{\"valid\":\"start of next record... cut",  // malformed
            JsonRecord("s", null, "2026-05-03T10:00:00Z"),
        });
        var reader = new JsonlSliceReader(_tempFile);
        var slice = reader.ReadSlice(new JsonlSliceFilter());
        Assert.Single(slice);
    }

    [Fact(DisplayName = "BR-DEMO-004 — RawLine preserves original input byte-for-byte for verbatim embedding")]
    public void RawLine_Preserved()
    {
        var line = JsonRecord("s", null, "2026-05-03T10:00:00Z");
        File.WriteAllLines(_tempFile, new[] { line });
        var reader = new JsonlSliceReader(_tempFile);
        var slice = reader.ReadSlice(new JsonlSliceFilter());
        Assert.Single(slice);
        Assert.Equal(line, slice[0].RawLine);
    }

    /// <summary>
    /// Build a minimal OTLP/JSON-shaped record with the optional
    /// session.id and plan attributes plus a top-level timestamp.
    /// </summary>
    private static string JsonRecord(string sessionId, string? planTag, string timestamp)
    {
        var attrs = new List<string> { $"{{\"key\":\"session.id\",\"value\":{{\"stringValue\":\"{sessionId}\"}}}}" };
        if (planTag is not null)
            attrs.Add($"{{\"key\":\"plan\",\"value\":{{\"stringValue\":\"{planTag}\"}}}}");
        return $"{{\"timestamp\":\"{timestamp}\",\"resourceSpans\":[{{\"resource\":{{\"attributes\":[{string.Join(",", attrs)}]}},\"scopeSpans\":[{{\"spans\":[{{\"name\":\"test-span\"}}]}}]}}]}}";
    }
}
