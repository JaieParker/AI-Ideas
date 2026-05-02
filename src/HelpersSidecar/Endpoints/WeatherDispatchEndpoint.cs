namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /weather skill. Accepts a form-encoded
/// args field, calls https://wttr.in (the only allowed egress target
/// — BR-SKILL-005), returns plain text.
/// </summary>
public static class WeatherDispatchEndpoint
{
    private const string WttrInBase = "https://wttr.in";
    private const int TimeoutSeconds = 5;

    private static readonly HttpClient Http = new(new SocketsHttpHandler())
    {
        Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
    };

    public static IEndpointRouteBuilder MapWeatherDispatch(this IEndpointRouteBuilder app)
    {
        // (Delegate) cast forces the route-handler overload (which awaits
        // and writes the IResult) instead of RequestDelegate (which would
        // discard the IResult return value).
        app.MapPost("/skills/weather/dispatch", (Delegate)Handle)
            .WithName("WeatherDispatch")
            .WithSummary("Skill dispatcher for /weather")
            .WithDescription("Form-encoded args (the user's location). Calls wttr.in " +
                "with the value URL-encoded into the path. Returns plain text suitable " +
                "for embedding in the prompt.");
        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx)
    {
        var form = await ctx.Request.ReadFormAsync();
        var location = form["args"].ToString().Trim();
        var url = $"{WttrInBase}/{Uri.EscapeDataString(location)}?format=3";
        try
        {
            var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return Results.Text($"weather lookup failed: HTTP {(int)response.StatusCode}", "text/plain");
            var text = (await response.Content.ReadAsStringAsync()).Trim();
            return Results.Text(text.Length > 0 ? text : "(no weather data returned)", "text/plain");
        }
        catch (Exception ex)
        {
            return Results.Text($"weather lookup failed: {ex.Message}", "text/plain");
        }
    }
}
