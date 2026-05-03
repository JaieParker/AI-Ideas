using System.Net;
using System.Net.Http.Headers;
using HelpersSidecar.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpersSidecar.Tests.Endpoints;

/// <summary>
/// BR-PROCESS-001 / BR-EXTEND-006 — /skills/extend-skills/dispatch
/// performs deterministic gathering for the multi-phase flow,
/// parameterised by the resolved <c>IDomain</c>'s
/// <see cref="HelpersSidecar.Domain.PlanFileConventions"/>. Never
/// mutates anything.
/// </summary>
public class ExtendSkillsDispatchEndpointTests
{
    [Fact(DisplayName = "BR-EXTEND-007 — empty args returns usage with the list of known domains")]
    public async Task Empty_Args_Returns_Usage_With_Known_Domains()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", ""), ("skill_dir", "")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("usage:", text);
        Assert.Contains("known domains:", text);
        Assert.Contains("otel", text);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — `<domain>` alone returns the Begin gathering message")]
    public async Task Domain_Only_Returns_Begin_Message()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel"), ("skill_dir", "")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("phase 0 gathering", text);
        Assert.Contains("domain: otel", text);
        Assert.Contains("Phase 0", text);
        Assert.Contains("Phase 4", text);
    }

    [Fact(DisplayName = "BR-EXTEND-009 — Begin emits a structured PLAN_TAG_ENRICHMENT directive with the next plan filename")]
    public async Task Begin_Includes_Plan_Enrichment_Reminder()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md");
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("PLAN_TAG_ENRICHMENT", text);
        Assert.Contains("/enrich plan", text);
        Assert.Contains("The-OTEL-Plan-2.md", text);
        Assert.Contains("BR-EXTEND-009", text);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — Begin lists Phase 1.5 (architecture-review) between Phase 1 and Phase 2")]
    public async Task Begin_Lists_Phase_1_5_Architecture_Review()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md");
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("Phase 1.5", text);
        Assert.Contains("/architecture-review", text);
        Assert.Contains("BR-PROCESS-009", text);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — Begin reports the next plan filename from the resolved domain's PlanFileConventions")]
    public async Task Begin_Reports_Next_Plan_File()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md", "The-OTEL-Plan-3.md");
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The-OTEL-Plan-4.md", text);  // max(1,3)+1
    }

    [Fact(DisplayName = "BR-EXTEND-005 — Begin normalises the topic into a slug")]
    public async Task Begin_Normalises_Topic_To_Slug()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md");
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel Fix THE foo bar"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The-OTEL-Plan-2-fix-the-foo-bar.md", text);
        Assert.Contains("'Fix THE foo bar' → 'fix-the-foo-bar'", text);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — `<domain> status` returns a status report")]
    public async Task Status_Returns_Status_Report()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel status"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("/extend-skills status", text);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — `<domain> revert` returns a recent-extend-flow-commits message")]
    public async Task Revert_Returns_Recent_Flow_Commits_Message()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", "otel revert"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("recent extend-flow commits", text);
        Assert.Contains("domain: otel", text);
    }

    [Fact(DisplayName = "BR-EXTEND-006 — unknown domain returns an error naming KnownNames")]
    public async Task Unknown_Domain_Returns_Error()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", "s1"), ("args", "not-a-real-domain"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("unknown domain", text);
        Assert.Contains("not-a-real-domain", text);
        Assert.Contains("known:", text);
        Assert.Contains("otel", text);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — empty session_id is rejected")]
    public async Task Missing_SessionId_Rejected()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/extend-skills/dispatch",
            FormContent(("session_id", ""), ("args", "otel"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("no session id", text);
    }

    // ---------------- helpers ----------------

    private static FormUrlEncodedContent FormContent(params (string K, string V)[] kv)
    {
        var content = new FormUrlEncodedContent(kv.Select(p => new KeyValuePair<string, string>(p.K, p.V)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return content;
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
