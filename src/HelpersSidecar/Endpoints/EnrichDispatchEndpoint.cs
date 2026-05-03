using System.Text;
using System.Text.Json;
using HelpersSidecar.Application;
using HelpersSidecar.Infrastructure;
using Microsoft.Extensions.Options;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /enrich skill. Parses the verb,
/// translates to a collector control-API call, renders human text.
///
/// Per <c>BR-OTEL-007</c>, the "collector down" error message
/// names the collector's host:port resolved from
/// <see cref="CollectorOptions"/>, never an inline literal.
/// </summary>
public static class EnrichDispatchEndpoint
{
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

    private static async Task<IResult> Handle(HttpContext ctx, ICollectorControlClient collector,
        IOptions<CollectorOptions> opts)
    {
        var form = await ctx.Request.ReadFormAsync();
        var sessionId = form["session_id"].ToString().Trim();
        var args = form["args"].ToString();

        if (string.IsNullOrEmpty(sessionId))
            return Results.Text("enrich failed: no session id provided", "text/plain");

        var down = CollectorDownMessage(opts.Value);
        var verb = EnrichVerb.Parse(args);

        var resp = verb.Kind switch
        {
            EnrichVerbKind.Usage  => Result(Usage),
            EnrichVerbKind.Show   => await Show(collector, sessionId, down),
            EnrichVerbKind.Set    => await Set(collector, sessionId, verb.Key!, verb.Value!, down),
            EnrichVerbKind.Remove => await Remove(collector, sessionId, verb.Key!, down),
            EnrichVerbKind.Clear  => await Clear(collector, sessionId, down),
            _                     => Result(Usage),
        };
        return resp;
    }

    private static string CollectorDownMessage(CollectorOptions o) =>
        $"enrich failed: collector control API not reachable on {o.CollectorHost}:{o.CollectorControlPort}. " +
        "Run /otel to start the collector and try again.";

    private static async Task<IResult> Show(ICollectorControlClient collector, string sessionId, string down)
    {
        var r = await collector.GetSessionEnrichmentsAsync(sessionId);
        if (r is null) return Result(down);
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

    private static async Task<IResult> Set(ICollectorControlClient collector, string sessionId, string key, string value, string down)
    {
        var r = await collector.SetSessionEnrichmentAsync(sessionId, key, value);
        if (r is null) return Result(down);
        if (r.StatusCode != 200) return Result($"enrich failed: HTTP {r.StatusCode}: {r.Body}");
        return Result($"set {key}={value}");
    }

    private static async Task<IResult> Remove(ICollectorControlClient collector, string sessionId, string key, string down)
    {
        var r = await collector.RemoveSessionEnrichmentAsync(sessionId, key);
        if (r is null) return Result(down);
        if (r.StatusCode != 200) return Result($"enrich failed: HTTP {r.StatusCode}: {r.Body}");
        return Result($"removed {key}");
    }

    private static async Task<IResult> Clear(ICollectorControlClient collector, string sessionId, string down)
    {
        var r = await collector.ClearSessionEnrichmentsAsync(sessionId);
        if (r is null) return Result(down);
        if (r.StatusCode != 200) return Result($"enrich failed: HTTP {r.StatusCode}: {r.Body}");
        return Result("cleared all per-session enrichments");
    }

    private static IResult Result(string text) => Results.Text(text, "text/plain");
}
