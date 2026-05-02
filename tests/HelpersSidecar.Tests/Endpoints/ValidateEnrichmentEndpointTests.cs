using System.Net;
using System.Net.Http.Json;
using HelpersSidecar.Endpoints;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-ENRICH-001 / BR-ENRICH-002 / BR-ENRICH-003 wired through HTTP.
/// </summary>
public class ValidateEnrichmentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ValidateEnrichmentEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact(DisplayName = "BR-ENRICH-001/002 — valid pair returns 200 with no warnings")]
    public async Task Valid_Pair_Returns_200_No_Warnings()
    {
        var response = await _client.PostAsJsonAsync(
            "/helpers/enrichments/validate",
            new ValidateEnrichmentRequest("ticket.id", "PROJ-1234"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidateEnrichmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("ticket.id", body!.Key);
        Assert.Equal("PROJ-1234", body.Value);
        Assert.Empty(body.Warnings);
    }

    [Theory(DisplayName = "BR-ENRICH-001 — invalid keys 400")]
    [InlineData("Team")]
    [InlineData("1team")]
    [InlineData("team!")]
    [InlineData("")]
    public async Task Invalid_Key_Returns_400(string key)
    {
        var response = await _client.PostAsJsonAsync(
            "/helpers/enrichments/validate",
            new ValidateEnrichmentRequest(key, "value"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Error));
    }

    [Fact(DisplayName = "BR-ENRICH-002 — value over 4096 chars returns 400")]
    public async Task Value_Over_Limit_Returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/helpers/enrichments/validate",
            new ValidateEnrichmentRequest("team", new string('x', 4097)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Contains("4096", body!.Error);
    }

    [Fact(DisplayName = "BR-ENRICH-003 — secret-shaped value returns 200 with warning")]
    public async Task Secret_Shaped_Value_Returns_200_With_Warning()
    {
        var response = await _client.PostAsJsonAsync(
            "/helpers/enrichments/validate",
            new ValidateEnrichmentRequest("github.token", "ghp_abcdefghijklmnop"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidateEnrichmentResponse>();
        Assert.NotNull(body);
        Assert.Equal("ghp_abcdefghijklmnop", body!.Value);
        Assert.Single(body.Warnings);
        Assert.Contains("secret", body.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }
}
