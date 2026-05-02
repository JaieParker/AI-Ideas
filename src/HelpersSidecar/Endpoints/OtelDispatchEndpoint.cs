using System.Text;
using System.Text.Json;
using HelpersSidecar.Application;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /otel skill. Handles every /otel verb
/// in one place; emits an EXTEND_REQUESTED marker for the chained
/// /otel-extend skill (BR-SKILL — chaining via the Skill tool).
/// </summary>
public static class OtelDispatchEndpoint
{
    private const string CollectorDownMessage =
        "collector control API not reachable on 127.0.0.1:13133. " +
        "The Go collector may not be built/running yet.";

    private const string Usage =
        "usage: /otel [on|off|status|restart|help|set <k>:<v>|get <k>...|unset <k>|config [clear]|extend [topic]]";

    public static IEndpointRouteBuilder MapOtelDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/otel/dispatch", Handle)
            .WithName("OtelDispatch")
            .WithSummary("Skill dispatcher for /otel")
            .WithDescription("Form-encoded session_id, args, and skill_dir. Skill_dir " +
                "lets the dispatcher read HELP.md from the same skill directory the user " +
                "invoked, so the canonical command list has a single source of truth.");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, ICollectorControlClient collector)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        var args = form["args"].ToString();
        var skillDir = form["skill_dir"].ToString().Trim();

        if (string.IsNullOrEmpty(sessionId))
            return Text("otel failed: no session id provided");

        var verb = OtelVerb.Parse(args);

        return verb.Kind switch
        {
            OtelVerbKind.Usage       => Text(Usage),
            OtelVerbKind.Setup       => await Setup(collector, skillDir),
            OtelVerbKind.Help        => Text(ReadHelp(skillDir)),
            OtelVerbKind.On          => await Toggle(collector, sessionId, true),
            OtelVerbKind.Off         => await Toggle(collector, sessionId, false),
            OtelVerbKind.Status      => await Status(collector, sessionId, skillDir),
            OtelVerbKind.Restart     => await Restart(collector),
            OtelVerbKind.Set         => await SetPersistent(collector, verb.Key!, verb.Value!),
            OtelVerbKind.Unset       => await UnsetPersistent(collector, verb.Key!),
            OtelVerbKind.Get         => await GetPersistent(collector, verb.Key!),
            OtelVerbKind.GetMany     => await GetPersistentMany(collector, verb.Keys!),
            OtelVerbKind.Config      => await Config(collector),
            OtelVerbKind.ConfigClear => await ConfigClear(collector),
            OtelVerbKind.Extend      => Text(ExtendMarker(verb.Topic)),
            _                         => Text(Usage),
        };
    }

    // ---------------- handlers ----------------

    private static async Task<IResult> Setup(ICollectorControlClient collector, string skillDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Helpers sidecar  : ok (you are reading this from it)");
        sb.AppendLine(await collector.IsHealthyAsync()
            ? "Collector control: ok"
            : "Collector control: not running (Go collector not yet built — see The-OTEL-Plan.md)");
        sb.AppendLine();
        sb.Append(ReadHelp(skillDir));
        return Text(sb.ToString());
    }

    private static async Task<IResult> Status(ICollectorControlClient collector, string sessionId, string skillDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Session          : {sessionId}");
        sb.AppendLine(await collector.IsHealthyAsync()
            ? "Collector control: ok"
            : "Collector control: not reachable");

        var col = await collector.GetSessionCollectionAsync(sessionId);
        sb.AppendLine($"Collection       : {(col is { StatusCode: 200 } ? col.Body : "unknown")}");

        var er = await collector.GetSessionEnrichmentsAsync(sessionId);
        sb.AppendLine(er is { StatusCode: 200 }
            ? $"Per-session     : {er.Body}"
            : "Per-session     : unknown");

        var pp = await collector.GetPersistentAsync(null);
        sb.AppendLine(pp is { StatusCode: 200 }
            ? $"Persistent      : {pp.Body}"
            : "Persistent      : unknown");

        return Text(sb.ToString());
    }

    private static async Task<IResult> Toggle(ICollectorControlClient collector, string sessionId, bool enabled)
    {
        var r = await collector.SetSessionCollectionAsync(sessionId, enabled);
        if (r is null) return Text(CollectorDownMessage);
        if (r.StatusCode != 200) return Text($"otel failed: HTTP {r.StatusCode}: {r.Body}");
        return Text(enabled ? "collection enabled for this session" : "collection paused for this session");
    }

    private static async Task<IResult> Restart(ICollectorControlClient collector)
    {
        var r = await collector.RestartAsync();
        return Text(r is null ? CollectorDownMessage
            : r.StatusCode == 200 ? "collector restart triggered"
            : $"otel failed: HTTP {r.StatusCode}: {r.Body}");
    }

    private static async Task<IResult> SetPersistent(ICollectorControlClient collector, string key, string value)
    {
        var r = await collector.SetPersistentAsync(key, value);
        if (r is null) return Text(CollectorDownMessage);
        if (r.StatusCode != 200) return Text($"otel failed: HTTP {r.StatusCode}: {r.Body}");
        return Text($"persistent set {key}={value}");
    }

    private static async Task<IResult> UnsetPersistent(ICollectorControlClient collector, string key)
    {
        var r = await collector.RemovePersistentAsync(key);
        if (r is null) return Text(CollectorDownMessage);
        if (r.StatusCode != 200) return Text($"otel failed: HTTP {r.StatusCode}: {r.Body}");
        return Text($"persistent removed {key}");
    }

    private static async Task<IResult> GetPersistent(ICollectorControlClient collector, string key)
    {
        var r = await collector.GetPersistentAsync(key);
        if (r is null) return Text(CollectorDownMessage);
        return r.StatusCode switch
        {
            200 => Text($"{key}={r.Body}"),
            404 => Text($"{key}=(unset)"),
            _   => Text($"otel failed: HTTP {r.StatusCode}: {r.Body}"),
        };
    }

    private static async Task<IResult> GetPersistentMany(ICollectorControlClient collector, string[] keys)
    {
        var r = await collector.GetPersistentManyAsync(keys);
        if (r is null) return Text(CollectorDownMessage);
        if (r.StatusCode != 200) return Text($"otel failed: HTTP {r.StatusCode}: {r.Body}");
        try
        {
            var arr = JsonDocument.Parse(r.Body).RootElement;
            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                var k = item.GetProperty("key").GetString();
                var exists = item.GetProperty("exists").GetBoolean();
                var v = exists ? item.GetProperty("value").GetString() : "(unset)";
                sb.AppendLine($"{k}={v}");
            }
            return Text(sb.ToString().TrimEnd());
        }
        catch (JsonException) { return Text($"otel failed: invalid response: {r.Body}"); }
    }

    private static async Task<IResult> Config(ICollectorControlClient collector)
    {
        var r = await collector.GetPersistentAsync(null);
        if (r is null) return Text(CollectorDownMessage);
        if (r.StatusCode != 200) return Text($"otel failed: HTTP {r.StatusCode}: {r.Body}");
        try
        {
            var doc = JsonDocument.Parse(r.Body);
            var sb = new StringBuilder();
            var any = false;
            foreach (var prop in doc.RootElement.EnumerateObject().OrderBy(p => p.Name))
            {
                sb.AppendLine($"{prop.Name}={prop.Value.GetString()}");
                any = true;
            }
            return Text(any ? sb.ToString().TrimEnd() : "(no persistent enrichments set)");
        }
        catch (JsonException) { return Text($"otel failed: invalid response: {r.Body}"); }
    }

    private static async Task<IResult> ConfigClear(ICollectorControlClient collector)
    {
        // BR-ENRICH-011 confirmation is enforced at the SKILL layer
        // (the user types `/otel config clear --yes` or similar). The
        // dispatcher executes whatever the user explicitly invoked.
        var r = await collector.ClearPersistentAsync();
        if (r is null) return Text(CollectorDownMessage);
        if (r.StatusCode != 200) return Text($"otel failed: HTTP {r.StatusCode}: {r.Body}");
        return Text("cleared all persistent enrichments");
    }

    private static string ExtendMarker(string? topic)
        => $"EXTEND_REQUESTED: topic=\"{topic ?? string.Empty}\"\n" +
           "Invoke the otel-extend skill via the Skill tool with the topic above.";

    private static string ReadHelp(string skillDir)
    {
        if (string.IsNullOrEmpty(skillDir)) return "(HELP.md unavailable: no skill_dir provided)";
        var path = Path.Combine(skillDir, "HELP.md");
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return $"(HELP.md not found at {path})"; }
        catch (DirectoryNotFoundException) { return $"(skill dir not found: {skillDir})"; }
    }

    private static IResult Text(string text) => Results.Text(text, "text/plain");
}
