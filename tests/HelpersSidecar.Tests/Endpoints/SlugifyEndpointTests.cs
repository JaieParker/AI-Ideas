using System.Net;
using System.Net.Http.Json;
using HelpersSidecar.Endpoints;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-EXTEND-005 — POST /helpers/topics/slugify wraps TopicSlug.TryCreate
/// behind an HTTP boundary. Success returns 200 with the slug; failure
/// returns 400 with a human-readable error.
/// </summary>
public class SlugifyEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SlugifyEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory(DisplayName = "BR-EXTEND-005 — slugify returns 200 with the normalised slug")]
    [InlineData("Fix the foo bar", "fix-the-foo-bar")]
    [InlineData("FOO", "foo")]
    [InlineData("  spaces  ", "spaces")]
    [InlineData("foo--bar", "foo-bar")]
    public async Task Slugify_Returns_200_With_Slug(string input, string expected)
    {
        var response = await _client.PostAsJsonAsync(
            "/helpers/topics/slugify", new SlugifyRequest(input));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SlugifyResponse>();
        Assert.NotNull(body);
        Assert.Equal(expected, body!.Slug);
    }

    [Theory(DisplayName = "BR-EXTEND-005 — slugify returns 400 when input normalises to empty")]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("   ")]
    [InlineData("---")]
    public async Task Slugify_Returns_400_For_Empty_Normalisation(string input)
    {
        var response = await _client.PostAsJsonAsync(
            "/helpers/topics/slugify", new SlugifyRequest(input));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Error));
    }
}
