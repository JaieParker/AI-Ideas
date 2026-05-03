using System.Net;
using System.Net.Http.Json;
using HelpersSidecar.Endpoints;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-EXTEND-011 — /helpers/integration-test-scope is a thin
/// pass-through to DomainImpactScope so non-.NET callers can
/// talk to the resolver. Behaviour tests live on the
/// DomainImpactScope unit; this class only verifies the HTTP
/// shape (request decoding, response shape, error case).
/// </summary>
public class IntegrationTestScopeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTestScopeEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "BR-EXTEND-011 — endpoint returns impacted domains for a per-domain change")]
    public async Task Per_Domain_Change_Returns_Impacted_Domain()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/helpers/integration-test-scope",
            new { ChangedPaths = new[] { "docs/otel/plans/The-OTEL-Plan.md" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationTestScopeResponse>();
        Assert.NotNull(body);
        Assert.Contains("otel", body!.ImpactedDomains);
        Assert.False(body.CrossDomainTriggered);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — endpoint flags cross-domain trigger for top-level shared file")]
    public async Task Cross_Domain_File_Flags_CrossDomainTriggered()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/helpers/integration-test-scope",
            new { ChangedPaths = new[] { "docs/business-rules.md" } });

        var body = await response.Content.ReadFromJsonAsync<IntegrationTestScopeResponse>();
        Assert.NotNull(body);
        Assert.True(body!.CrossDomainTriggered);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — endpoint emits a TestFilterHint suitable for dotnet test --filter")]
    public async Task TestFilterHint_Is_Emitted()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/helpers/integration-test-scope",
            new { ChangedPaths = new[] { "docs/otel/plans/The-OTEL-Plan.md" } });

        var body = await response.Content.ReadFromJsonAsync<IntegrationTestScopeResponse>();
        Assert.NotNull(body);
        Assert.Contains("FullyQualifiedName~Otel", body!.TestFilterHint);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — empty ChangedPaths returns empty impact + empty TestFilterHint")]
    public async Task Empty_Changed_Paths_Returns_Empty_Result()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/helpers/integration-test-scope",
            new { ChangedPaths = Array.Empty<string>() });

        var body = await response.Content.ReadFromJsonAsync<IntegrationTestScopeResponse>();
        Assert.NotNull(body);
        Assert.Empty(body!.ImpactedDomains);
        Assert.False(body.CrossDomainTriggered);
        Assert.Equal(string.Empty, body.TestFilterHint);
    }

    [Fact(DisplayName = "BR-EXTEND-011 — null ChangedPaths returns 400")]
    public async Task Null_Changed_Paths_Returns_BadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/helpers/integration-test-scope",
            new { ChangedPaths = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
