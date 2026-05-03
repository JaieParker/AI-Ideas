using System.Text.Json;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Reads <c>output/telemetry.jsonl</c>, optionally filters records
/// by timestamp window + session id (or plan enrichment), and
/// returns a structured slice for Plan-8's
/// <c>MarkdownDemoReportWriter</c> to embed in the demo report.
///
/// File-share semantics follow BR-DEMO-003: open with
/// <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/>
/// so the read coexists with the collector's exporter holding
/// the file open for append.
///
/// Malformed JSONL lines are skipped, not fatal — collector
/// flush boundaries can land mid-line; resilience is part of the
/// reader's contract.
/// </summary>
public sealed class JsonlSliceReader
{
    private readonly string _path;

    public JsonlSliceReader(string path)
    {
        _path = path;
    }

    /// <summary>
    /// Read every line in the file as a <see cref="JsonlRecord"/>;
    /// skip lines that don't parse. The window + session filter is
    /// applied inline so callers don't need to materialise the full
    /// file when they only want a slice.
    /// </summary>
    public IReadOnlyList<JsonlRecord> ReadSlice(JsonlSliceFilter filter)
    {
        if (!File.Exists(_path))
            return Array.Empty<JsonlRecord>();

        var results = new List<JsonlRecord>();
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (TryParse(line, out var record) && filter.Matches(record))
                    results.Add(record);
            }
        }
        catch (IOException)
        {
            // BR-DEMO-003 — caller renders an empty slice as "couldn't
            // read JSONL" rather than throwing.
        }
        return results;
    }

    private static bool TryParse(string line, out JsonlRecord record)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var ts = TryReadTimestamp(root);
            var sessionId = TryReadAttribute(root, "session.id");
            var planTag = TryReadAttribute(root, "plan");
            record = new JsonlRecord(line, ts, sessionId, planTag);
            return true;
        }
        catch (JsonException)
        {
            record = default!;
            return false;
        }
    }

    private static DateTimeOffset? TryReadTimestamp(JsonElement root)
    {
        // The collector's fileexporter writes OTLP/JSON. Records can
        // expose timestamps in several places (resourceSpans → span
        // startTimeUnixNano, resourceLogs → log timeUnixNano, etc.).
        // For Plan-8 v1 we look for a top-level "timestamp" property
        // OR descend into the first span / log to read its
        // startTimeUnixNano. Records without a parseable timestamp
        // get null and are kept (the report appendix may still want
        // them).
        if (root.TryGetProperty("timestamp", out var tsProp) &&
            tsProp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(tsProp.GetString(), out var parsed))
            return parsed;

        // Try resourceSpans[0].scopeSpans[0].spans[0].startTimeUnixNano.
        if (root.TryGetProperty("resourceSpans", out var rs) &&
            rs.ValueKind == JsonValueKind.Array && rs.GetArrayLength() > 0)
        {
            var first = rs[0];
            if (first.TryGetProperty("scopeSpans", out var ss) &&
                ss.ValueKind == JsonValueKind.Array && ss.GetArrayLength() > 0)
            {
                var firstScope = ss[0];
                if (firstScope.TryGetProperty("spans", out var spans) &&
                    spans.ValueKind == JsonValueKind.Array && spans.GetArrayLength() > 0)
                {
                    var firstSpan = spans[0];
                    if (firstSpan.TryGetProperty("startTimeUnixNano", out var nanoProp))
                    {
                        var nanoStr = nanoProp.ValueKind == JsonValueKind.String
                            ? nanoProp.GetString()
                            : nanoProp.ToString();
                        if (long.TryParse(nanoStr, out var nano))
                            return DateTimeOffset.FromUnixTimeMilliseconds(nano / 1_000_000L);
                    }
                }
            }
        }
        return null;
    }

    private static string? TryReadAttribute(JsonElement root, string key)
    {
        // Walk resourceSpans[*].resource.attributes[*] looking for
        // {key: <key>, value: {stringValue: <v>}}. Same shape for
        // resourceLogs / resourceMetrics; we walk all three.
        foreach (var topLevel in new[] { "resourceSpans", "resourceLogs", "resourceMetrics" })
        {
            if (!root.TryGetProperty(topLevel, out var arr) ||
                arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var resource in arr.EnumerateArray())
            {
                if (!resource.TryGetProperty("resource", out var res)) continue;
                if (!res.TryGetProperty("attributes", out var attrs) ||
                    attrs.ValueKind != JsonValueKind.Array) continue;
                foreach (var attr in attrs.EnumerateArray())
                {
                    if (!attr.TryGetProperty("key", out var k) ||
                        k.ValueKind != JsonValueKind.String ||
                        k.GetString() != key) continue;
                    if (attr.TryGetProperty("value", out var v) &&
                        v.TryGetProperty("stringValue", out var sv) &&
                        sv.ValueKind == JsonValueKind.String)
                        return sv.GetString();
                }
            }
        }
        return null;
    }
}

/// <summary>
/// One record from <c>output/telemetry.jsonl</c>, parsed enough to
/// filter on but kept as the original <see cref="RawLine"/> for
/// embedding in the report verbatim.
/// </summary>
public sealed record JsonlRecord(
    string RawLine,
    DateTimeOffset? Timestamp,
    string? SessionId,
    string? PlanTag);

/// <summary>
/// Filter for <see cref="JsonlSliceReader.ReadSlice"/>. All non-null
/// fields are AND'd; null fields are wildcards. Window matches when
/// <see cref="StartedAt"/> ≤ record.Timestamp ≤ <see cref="EndedAt"/>
/// (inclusive). When the record has no timestamp and a window is set,
/// the record is excluded.
/// </summary>
public sealed record JsonlSliceFilter(
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null,
    string? SessionId = null,
    string? PlanTag = null)
{
    public bool Matches(JsonlRecord r)
    {
        if (SessionId is not null && r.SessionId != SessionId) return false;
        if (PlanTag is not null && r.PlanTag != PlanTag) return false;
        if (StartedAt is not null || EndedAt is not null)
        {
            if (r.Timestamp is null) return false;
            if (StartedAt is not null && r.Timestamp.Value < StartedAt.Value) return false;
            if (EndedAt is not null && r.Timestamp.Value > EndedAt.Value) return false;
        }
        return true;
    }
}
