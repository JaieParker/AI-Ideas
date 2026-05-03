using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-SKILL-013 — /skills/ai-level/dispatch HTTP shape only.
/// Behaviour of the underlying checker / parser / writer is
/// covered by their own unit tests; this fixture verifies the
/// wire surface (usage, error cases, scope rejection).
/// </summary>
public class AiLevelDispatchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AiLevelDispatchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "BR-SKILL-013 — empty args returns the usage message")]
    public async Task Empty_Args_Returns_Usage()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/skills/ai-level/dispatch",
            FormContent(("session_id", "s1"), ("args", "")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("usage:", text);
        Assert.Contains("local", text);
    }

    [Fact(DisplayName = "BR-SKILL-013 — missing session_id returns failure message")]
    public async Task Missing_Session_Id_Fails()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/skills/ai-level/dispatch",
            FormContent(("session_id", ""), ("args", "local")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("ai-level failed", text);
    }

    [Fact(DisplayName = "BR-SECURITY-003 — global scope is rejected with explanatory error")]
    public async Task Global_Scope_Rejected()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/skills/ai-level/dispatch",
            FormContent(("session_id", "s1"), ("args", "global")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("not supported in v1", text);
        Assert.Contains("BR-SECURITY-003", text);
    }

    [Fact(DisplayName = "BR-SECURITY-003 — 'all' scope is rejected the same as 'global'")]
    public async Task All_Scope_Rejected_Same_As_Global()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/skills/ai-level/dispatch",
            FormContent(("session_id", "s1"), ("args", "all")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("not supported in v1", text);
    }

    [Fact(DisplayName = "BR-SKILL-013 — unknown skill name returns helpful error")]
    public async Task Unknown_Skill_Returns_Helpful_Error()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/skills/ai-level/dispatch",
            FormContent(("session_id", "s1"), ("args", "definitely-not-a-real-skill")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("not found", text);
        Assert.Contains("definitely-not-a-real-skill", text);
    }

    private static FormUrlEncodedContent FormContent(params (string K, string V)[] kv)
    {
        var c = new FormUrlEncodedContent(kv.Select(p => new KeyValuePair<string, string>(p.K, p.V)));
        c.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return c;
    }
}
