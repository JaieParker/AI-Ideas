using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-EXTEND-006 / BR-EXTEND-008 — /skills/domain-info/dispatch
/// returns the requested slices of an IDomain in one response.
/// Read-only — never mutates anything. Slice projection: any
/// subset by name; `all` (or no slices arg) returns everything.
/// </summary>
public class DomainInfoDispatchTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DomainInfoDispatchTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "BR-EXTEND-006 — empty args returns usage with known domains and slice names")]
    public async Task Empty_Args_Returns_Usage()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("usage:", text);
        Assert.Contains("known domains:", text);
        Assert.Contains("otel", text);
        Assert.Contains("trusted-references", text);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — unknown domain returns an error naming KnownNames")]
    public async Task Unknown_Domain_Returns_Error()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "not-a-domain")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("unknown domain", text);
        Assert.Contains("not-a-domain", text);
        Assert.Contains("known:", text);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — `<domain>` with no slice arg returns ALL slices")]
    public async Task All_Slices_By_Default()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel")));

        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal("otel", json.GetProperty("domain").GetString());
        Assert.True(json.TryGetProperty("name", out _));
        Assert.True(json.TryGetProperty("plan-files", out _));
        Assert.True(json.TryGetProperty("commits", out _));
        Assert.True(json.TryGetProperty("governed-globs", out _));
        Assert.True(json.TryGetProperty("playbook-path", out _));
        Assert.True(json.TryGetProperty("glossary", out _));
        Assert.True(json.TryGetProperty("business-rules-path", out _));
        Assert.True(json.TryGetProperty("trusted-references", out _));
    }

    [Fact(DisplayName = "BR-EXTEND-006 — slice projection returns only the requested slices")]
    public async Task Slice_Projection_Returns_Only_Requested()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel glossary,plan-files")));

        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        Assert.True(json.TryGetProperty("glossary", out _));
        Assert.True(json.TryGetProperty("plan-files", out _));
        Assert.False(json.TryGetProperty("commits", out _));
        Assert.False(json.TryGetProperty("trusted-references", out _));
    }

    [Fact(DisplayName = "BR-EXTEND-008 — TrustedReferences slice contains the seeded Fowler/Evans/MS/OTel sources")]
    public async Task TrustedReferences_Includes_Seeded_Sources()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel trusted-references")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("martinfowler.com/bliki/BoundedContext.html", text);
        Assert.Contains("martinfowler.com/bliki/UbiquitousLanguage.html", text);
        Assert.Contains("DDD_Reference_2015-03.pdf", text);
        Assert.Contains("opentelemetry.io/docs/specs/otel", text);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — unknown slice name returns an error with the catalogue")]
    public async Task Unknown_Slice_Returns_Error()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel not-a-slice,plan-files")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("unknown slice", text);
        Assert.Contains("not-a-slice", text);
        Assert.Contains("trusted-references", text);  // catalogue is shown
    }

    [Fact(DisplayName = "BR-EXTEND-006 — `*` is an alias for all (parity with `all`)")]
    public async Task Star_Slice_Is_All()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel *")));

        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        Assert.True(json.TryGetProperty("trusted-references", out _));
        Assert.True(json.TryGetProperty("commits", out _));
    }

    [Fact(DisplayName = "BR-EXTEND-006 — plan-files slice exposes Directory (Plan-9)")]
    public async Task PlanFiles_Slice_Exposes_Directory()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel plan-files")));

        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        var planFiles = json.GetProperty("plan-files");
        Assert.True(planFiles.TryGetProperty("directory", out var dir));
        Assert.Equal("docs/otel/plans", dir.GetString());
    }

    [Fact(DisplayName = "BR-PROCESS-015 — artefacts slice projects registry entries by owner (Plan-11)")]
    public async Task Artefacts_Slice_Projects_By_Owner()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel artefacts")));

        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        Assert.True(json.TryGetProperty("artefacts", out var artefacts));

        var names = new List<string>();
        foreach (var e in artefacts.EnumerateArray())
            names.Add(e.GetProperty("name").GetString()!);

        // OTEL-owned artefacts include demo-report, telemetry, persistent-enrichments, plan-files.
        Assert.Contains("demo-report", names);
        Assert.Contains("telemetry", names);
        Assert.Contains("persistent-enrichments", names);
        Assert.Contains("plan-files", names);
        // Cross-domain artefacts MUST NOT appear under otel's projection.
        Assert.DoesNotContain("ai-level-report", names);
        Assert.DoesNotContain("plans-index", names);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — artefacts slice for cross-domain returns cross-domain artefacts")]
    public async Task Artefacts_Slice_Cross_Domain()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "cross-domain artefacts")));

        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        var artefacts = json.GetProperty("artefacts");
        var names = new List<string>();
        foreach (var e in artefacts.EnumerateArray())
            names.Add(e.GetProperty("name").GetString()!);

        Assert.Contains("ai-level-report", names);
        Assert.Contains("plans-index", names);
        Assert.Contains("business-rules", names);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — artefacts entries include destinations + lifecycle")]
    public async Task Artefacts_Entries_Include_Destination_And_Lifecycle()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/skills/domain-info/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel artefacts")));

        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        var demoReport = json.GetProperty("artefacts").EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "demo-report");

        Assert.Equal("OneShot", demoReport.GetProperty("lifecycle").GetString());
        var destinations = demoReport.GetProperty("destinations").EnumerateArray().ToList();
        Assert.Single(destinations);
        Assert.Equal("local-fs", destinations[0].GetProperty("name").GetString());
    }

    private static FormUrlEncodedContent FormContent(params (string K, string V)[] kv)
    {
        var c = new FormUrlEncodedContent(kv.Select(p => new KeyValuePair<string, string>(p.K, p.V)));
        c.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return c;
    }
}
