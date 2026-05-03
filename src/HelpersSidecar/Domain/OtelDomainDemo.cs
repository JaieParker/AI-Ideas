using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Domain;

/// <summary>
/// The OTEL domain's guided demo (BR-EXTEND-010). Walks 14 live
/// skill-chain steps demonstrating bring-up, configure (persistent
/// + per-session), observe, change, observe, tear-down.
///
/// Every action step invokes another skill via
/// <see cref="ISkillDispatchClient"/>; observation steps (9 + 13)
/// read <c>output/telemetry.jsonl</c> directly — that's read-only
/// verification of records the upstream skill chain produced, not
/// an action (BR-DEMO-002).
/// </summary>
public sealed class OtelDomainDemo : IDomainDemo
{
    private const string OutputFile  = "output/telemetry.jsonl";

    public string DomainName => "otel";

    public async Task<IReadOnlyList<DemoStepResult>> RunAsync(DemoContext ctx, CancellationToken ct = default)
    {
        var steps = new List<DemoStepResult>
        {
            await OtelUp(ctx, 1),
            await OtelSet(ctx, 2, "user", "Jaie"),
            await OtelSet(ctx, 3, "workstation", "LightningBlue"),
            await OtelSet(ctx, 4, "version", "0.001"),
            await OtelGet(ctx, 5, "user", expected: "Jaie"),
            await Enrich(ctx, 6, "ticket.id", "JA-0001"),
            await Weather(ctx, 7, "London"),
            await Weather(ctx, 8, "$(rm -rf /)"),
            JsonlSummary(9, "after JA-0001 set, before JA-0002"),
            await Enrich(ctx, 10, "ticket.id", "JA-0002"),
            await Weather(ctx, 11, "London"),
            await Weather(ctx, 12, "$(rm -rf /)"),
            JsonlSummary(13, "after JA-0002 set"),
            await OtelDown(ctx, 14),
        };
        return steps;
    }

    // ---------------------------------------------------------- skill-chain helpers

    private static async Task<DemoStepResult> OtelUp(DemoContext ctx, int n)
    {
        var label = "/otel up (bring collector up; idempotent)";
        var r = await ctx.Skills.DispatchAsync("otel", new Dictionary<string, string>
        {
            ["session_id"] = ctx.SessionId,
            ["args"]       = "up",
            ["skill_dir"]  = string.Empty,
        });
        var ok = r.IsSuccess && (r.Body.Contains("collector started") || r.Body.Contains("already running"));
        return new(n, label, ok, $"chain → /skills/otel/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<DemoStepResult> OtelDown(DemoContext ctx, int n)
    {
        var label = "/otel down (full lifecycle complete; system fully reversible)";
        var r = await ctx.Skills.DispatchAsync("otel", new Dictionary<string, string>
        {
            ["session_id"] = ctx.SessionId,
            ["args"]       = "down",
            ["skill_dir"]  = string.Empty,
        });
        var ok = r.IsSuccess && (r.Body.Contains("collector stopped") ||
                                 r.Body.Contains("already down") ||
                                 r.Body.Contains("zombie"));
        return new(n, label, ok, $"chain → /skills/otel/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<DemoStepResult> OtelSet(DemoContext ctx, int n, string k, string v)
    {
        var label = $"/otel set {k}:{v}";
        var r = await ctx.Skills.DispatchAsync("otel", new Dictionary<string, string>
        {
            ["session_id"] = ctx.SessionId,
            ["args"]       = $"set {k}:{v}",
        });
        return new(n, label, r.IsSuccess, $"chain → /skills/otel/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<DemoStepResult> OtelGet(DemoContext ctx, int n, string k, string expected)
    {
        var label = $"/otel get {k} (expect {expected} from earlier set)";
        var r = await ctx.Skills.DispatchAsync("otel", new Dictionary<string, string>
        {
            ["session_id"] = ctx.SessionId,
            ["args"]       = $"get {k}",
        });
        var matches = r.IsSuccess && r.Body.Contains(expected);
        return new(n, label, matches, $"chain → /skills/otel/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<DemoStepResult> Enrich(DemoContext ctx, int n, string k, string v)
    {
        var label = $"/enrich {k} {v}";
        var r = await ctx.Skills.DispatchAsync("enrich", new Dictionary<string, string>
        {
            ["session_id"] = ctx.SessionId,
            ["args"]       = $"{k} {v}",
        });
        return new(n, label, r.IsSuccess, $"chain → /skills/enrich/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static async Task<DemoStepResult> Weather(DemoContext ctx, int n, string location)
    {
        var label = $"/weather {location}";
        var r = await ctx.Skills.DispatchAsync("weather", new Dictionary<string, string>
        {
            ["session_id"] = ctx.SessionId,
            ["args"]       = location,
        });
        return new(n, label, r.IsSuccess, $"chain → /skills/weather/dispatch HTTP {r.StatusCode}: {Trim(r.Body)}");
    }

    private static DemoStepResult JsonlSummary(int n, string when)
    {
        var label = $"observe {OutputFile} ({when})";
        if (!File.Exists(OutputFile))
            return new(n, label, false, $"{OutputFile} does not exist yet — collector hasn't flushed");
        try
        {
            var info = new FileInfo(OutputFile);
            using var stream = new FileStream(OutputFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var ja1 = lines.Count(l => l.Contains("JA-0001"));
            var ja2 = lines.Count(l => l.Contains("JA-0002"));
            return new(n, label, true,
                $"{lines.Length} records, {info.Length} bytes; JA-0001 refs={ja1}, JA-0002 refs={ja2}");
        }
        catch (IOException ex)
        {
            return new(n, label, false, $"could not read {OutputFile}: {ex.Message}");
        }
    }

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length <= 80 ? s : s[..80] + "...";
    }
}
