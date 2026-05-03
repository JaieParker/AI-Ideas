using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-PROCESS-009 / BR-SKILL-012 — /skills/architecture-review/dispatch
/// loads context (CLAUDE.md, business-rules, recent plans, target body,
/// resolved domain's TrustedReferences) and renders a structured prompt
/// with the ARCHITECTURE_REVIEW v1 schema. Phase 2a — scaffolding;
/// downstream gate integration lands in later phases.
///
/// Per BR-PROCESS-007 the test scopes to the dispatch endpoint's
/// rendering shape; the analyst (Claude) is NOT exercised — its
/// response in production lives in the user's session, not the test.
/// </summary>
public class ArchitectureReviewDispatchTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ArchitectureReviewDispatchTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "BR-PROCESS-009 — empty args returns usage with known domains")]
    public async Task Empty_Args_Returns_Usage()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/architecture-review/dispatch",
            FormContent(("session_id", "s1"), ("args", "")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("usage:", text);
        Assert.Contains("known:", text);
        Assert.Contains("otel", text);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — missing session_id is rejected")]
    public async Task Missing_Session_Rejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/architecture-review/dispatch",
            FormContent(("session_id", ""), ("args", "some-target")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("no session id", text);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — unknown --domain= returns an error naming KnownNames")]
    public async Task Unknown_Domain_Returns_Error()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/architecture-review/dispatch",
            FormContent(("session_id", "s1"), ("args", "target.md --domain=not-a-real-domain")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("unknown domain", text);
        Assert.Contains("not-a-real-domain", text);
        Assert.Contains("known:", text);
    }

    [Fact(DisplayName = "BR-SKILL-012 — rendered prompt includes CLAUDE.md, business-rules, schema, and TrustedReferences allow-list")]
    public async Task Rendered_Prompt_Includes_Context()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/architecture-review/dispatch",
            FormContent(("session_id", "s1"), ("args", "The-OTEL-Plan-5-domain-interface-and-extend-skills-rename.md --domain=otel")));

        var text = await response.Content.ReadAsStringAsync();

        // Header naming the target + domain.
        Assert.Contains("Target:    The-OTEL-Plan-5-domain-interface-and-extend-skills-rename.md", text);
        Assert.Contains("Domain:    otel", text);

        // Each context section appears.
        Assert.Contains("CLAUDE.md", text);
        Assert.Contains("docs/business-rules.md", text);
        Assert.Contains("Recent plan files", text);
        Assert.Contains("Glossary terms", text);
        Assert.Contains("Governed globs", text);

        // TrustedReferences allow-list is rendered with at least one Fowler URL.
        Assert.Contains("TrustedReferences", text);
        Assert.Contains("martinfowler.com", text);

        // Response schema is embedded verbatim.
        Assert.Contains("ARCHITECTURE_REVIEW v1", text);
        Assert.Contains("PER-COMMITMENT EVALUATION", text);
        Assert.Contains("ARCHITECTURE_DECISION_REQUIRED", text);
        Assert.Contains("RECOMMENDATION", text);
        Assert.Contains("COMPATIBLE | VIOLATES | EXTENDS", text);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — domain defaults to 'otel' when --domain= is omitted")]
    public async Task Domain_Defaults_To_Otel()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/architecture-review/dispatch",
            FormContent(("session_id", "s1"), ("args", "some-target.md")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("Domain:    otel", text);
    }

    private static FormUrlEncodedContent FormContent(params (string K, string V)[] kv)
    {
        var c = new FormUrlEncodedContent(kv.Select(p => new KeyValuePair<string, string>(p.K, p.V)));
        c.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return c;
    }
}
