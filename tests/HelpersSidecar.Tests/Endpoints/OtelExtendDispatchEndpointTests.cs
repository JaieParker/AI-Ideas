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
/// BR-PROCESS-001 — /skills/otel-extend/dispatch performs deterministic
/// gathering for the multi-phase flow without making any changes.
/// </summary>
public class OtelExtendDispatchEndpointTests
{
    [Fact(DisplayName = "BR-PROCESS-001 — empty args returns the Begin gathering message")]
    public async Task Empty_Args_Returns_Begin_Message()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel-extend/dispatch",
            FormContent(("session_id", "s1"), ("args", ""), ("skill_dir", "")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("phase 0 gathering", text);
        Assert.Contains("Phase 0", text);
        Assert.Contains("Phase 4", text);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — Begin reports the next plan filename from the scanner")]
    public async Task Begin_Reports_Next_Plan_File()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md", "The-OTEL-Plan-3.md");
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel-extend/dispatch",
            FormContent(("session_id", "s1"), ("args", ""), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The-OTEL-Plan-4.md", text);  // max(1,3)+1
    }

    [Fact(DisplayName = "BR-EXTEND-005 — Begin normalises the topic into a slug and includes it in the next plan filename")]
    public async Task Begin_Normalises_Topic_To_Slug()
    {
        using var factory = FactoryWithFiles("The-OTEL-Plan.md");
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel-extend/dispatch",
            FormContent(("session_id", "s1"), ("args", "Fix THE foo bar"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The-OTEL-Plan-2-fix-the-foo-bar.md", text);
        Assert.Contains("'Fix THE foo bar' → 'fix-the-foo-bar'", text);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — `status` returns a status report (best-effort)")]
    public async Task Status_Returns_Status_Report()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel-extend/dispatch",
            FormContent(("session_id", "s1"), ("args", "status"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("/otel-extend status", text);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — `revert` returns a recent-extend-flow-commits message")]
    public async Task Revert_Returns_Recent_Flow_Commits_Message()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel-extend/dispatch",
            FormContent(("session_id", "s1"), ("args", "revert"), ("skill_dir", "")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("recent extend-flow commits", text);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — empty session_id is rejected")]
    public async Task Missing_SessionId_Rejected()
    {
        using var factory = FactoryWithFiles();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/skills/otel-extend/dispatch",
            FormContent(("session_id", ""), ("args", ""), ("skill_dir", "")));

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
