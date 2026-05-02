using System.Net;
using System.Net.Http.Json;
using HelpersSidecar.Endpoints;
using HelpersSidecar.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-EXTEND-004 — POST /helpers/plans/next-name returns the next
/// available plan filename. The directory scan is mocked so the test
/// drives only the endpoint and use-case logic.
/// </summary>
public class NextPlanNameEndpointTests
{
    [Fact(DisplayName = "BR-EXTEND-004 — only base on disk → returns The-OTEL-Plan-2.md")]
    public async Task Only_Base_Returns_Plan_2()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/helpers/plans/next-name",
            new NextPlanNameRequest("/fake/root"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NextPlanNameResponse>();
        Assert.NotNull(body);
        Assert.Equal("The-OTEL-Plan-2.md", body!.Name);
        Assert.Equal(2, body.Number);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — slug attaches to the next-N filename")]
    public async Task Slug_Attaches_To_Next()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/helpers/plans/next-name",
            new NextPlanNameRequest("/fake/root", "fix-the-foo"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NextPlanNameResponse>();
        Assert.NotNull(body);
        Assert.Equal("The-OTEL-Plan-2-fix-the-foo.md", body!.Name);
        Assert.Equal(2, body.Number);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — slug is normalised through TopicSlug rules")]
    public async Task Slug_Is_Normalised()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/helpers/plans/next-name",
            new NextPlanNameRequest("/fake/root", "Fix THE foo bar"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NextPlanNameResponse>();
        Assert.NotNull(body);
        Assert.Equal("The-OTEL-Plan-2-fix-the-foo-bar.md", body!.Name);
    }

    [Fact(DisplayName = "BR-EXTEND-005 — slug normalising to empty returns 400")]
    public async Task Empty_Normalised_Slug_Returns_400()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/helpers/plans/next-name",
            new NextPlanNameRequest("/fake/root", "!!!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — gaps in numbering are not filled (max + 1)")]
    public async Task Gaps_Not_Filled()
    {
        using var factory = FactoryWithFiles(
            "The-OTEL-Plan.md",
            "The-OTEL-Plan-3-foo.md",
            "The-OTEL-Plan-5.md");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/helpers/plans/next-name",
            new NextPlanNameRequest("/fake/root"));

        var body = await response.Content.ReadFromJsonAsync<NextPlanNameResponse>();
        Assert.Equal(6, body!.Number);
        Assert.Equal("The-OTEL-Plan-6.md", body.Name);
    }

    [Fact(DisplayName = "BR-EXTEND-004 — empty directory returns the base file")]
    public async Task Empty_Directory_Returns_Base()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/helpers/plans/next-name",
            new NextPlanNameRequest("/fake/root"));

        var body = await response.Content.ReadFromJsonAsync<NextPlanNameResponse>();
        Assert.Equal(1, body!.Number);
        Assert.Equal("The-OTEL-Plan.md", body.Name);
    }

    private static WebApplicationFactory<Program> FactoryWithFiles(params string[] files)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPlanDirectoryScanner>();
                services.AddSingleton<IPlanDirectoryScanner>(new FakeScanner(files));
            }));

    private sealed class FakeScanner(IReadOnlyList<string> files) : IPlanDirectoryScanner
    {
        public IReadOnlyList<string> ListPlanFileNames(string rootDirectory) => files;
    }
}
