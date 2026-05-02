using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-HELPERS-004 — /healthz returns structured liveness payload.
/// </summary>
public class HealthzEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthzEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact(DisplayName = "BR-HELPERS-004 — /healthz returns 200 with status=ok, uptime_s, version")]
    public async Task Healthz_Returns_200_With_Liveness_Payload()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthzPayload>();
        Assert.NotNull(body);
        Assert.Equal("ok", body!.status);
        Assert.True(body.uptime_s >= 0);
        Assert.False(string.IsNullOrWhiteSpace(body.version));
    }

    private sealed record HealthzPayload(string status, long uptime_s, string version);
}
