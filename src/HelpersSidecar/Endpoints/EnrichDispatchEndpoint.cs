using System.Text;
using System.Text.Json;
using HelpersSidecar.Application;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /enrich skill. Parses the verb,
/// translates to a collector control-API call, renders human text.
/// </summary>
public static class EnrichDispatchEndpoint
{
    private const string CollectorDownMessage =
        "enrich failed: collector control API not reachable on 127.0.0.1:13133. " +
        "Run /otel to start the collector and try again.";

    private const string Usage =
        "usage: /enrich <key> <value> | --remove <key> | --clear | --show";

    public static IEndpointRouteBuilder MapEnrichDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/enrich/dispatch", Handle)
            .WithName("EnrichDispatch")
            .WithSummary("Skill dispatcher for /enrich")
            .WithDescription("Form-encoded session_id and args. Parses the verb, " +
                "calls the collector control API, returns plain text.");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, ICollectorControlClient collector)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        var args = form["args"].ToString();

        if (string.IsNullOrEmpty(sessionId))
            return Results.Text("enrich failed: no session id provided", "text/plain");

        var verb = EnrichVerb.Parse(args);

        var resp = verb.Kind switch
        {
            EnrichVerbKind.Usage  => Result(Usage),
            EnrichVerbKind.Show   => await Show(collector, sessionId),
            EnrichVerbKind.Set    => await Set(collector, sessionId, verb.Key!, verb.Value!),
            EnrichVerbKind.Remove => await Remove(collector, sessionId, verb.Key!),
            EnrichVerbKind.Clear  => await Clear(collector, sessionId),
            _                     => Result(Usage),
        };
        return resp;
    }

    private static async Task<IResult> Show(ICollectorControlClient collector, string sessionId)
    {
        var r = await collector.GetSessionEnrichmentsAsync(sessionId);
        if (r is null) return Result(CollectorDownMessage);
        if (r.StatusCode != 200) return Result($"enrich failed: HTTP {r.StatusCode}: {r.Body}");

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
            return Result(any ? sb.ToString().TrimEnd() : "(no enrichments set on this session)");
        }
        catch (JsonException) { return Result($"enrich failed: invalid response: {r.Body}"); }
    }

    private static async Task<IResult> Set(ICollectorControlClient collector, string sessionId, string key, string value)
    {
        var r = await collector.SetSessionEnrichmentAsync(sessionId, key, value);
        if (r is null) return Result(CollectorDownMessage);
        if (r.StatusCode != 200) return Result($"enrich failed: HTTP {r.StatusCode}: {r.Body}");
        return Result($"set {key}={value}");
    }

    private static async Task<IResult> Remove(ICollectorControlClient collector, string sessionId, string key)
    {
        var r = await collector.RemoveSessionEnrichmentAsync(sessionId, key);
        if (r is null) return Result(CollectorDownMessage);
        if (r.StatusCode != 200) return Result($"enrich failed: HTTP {r.StatusCode}: {r.Body}");
        return Result($"removed {key}");
    }

    private static async Task<IResult> Clear(ICollectorControlClient collector, string sessionId)
    {
        var r = await collector.ClearSessionEnrichmentsAsync(sessionId);
        if (r is null) return Result(CollectorDownMessage);
        if (r.StatusCode != 200) return Result($"enrich failed: HTTP {r.StatusCode}: {r.Body}");
        return Result("cleared all per-session enrichments");
    }

    private static IResult Result(string text) => Results.Text(text, "text/plain");
}
