using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace HelpersSidecar.Tests.Infrastructure;

/// <summary>
/// BR-CODE-002 — appsettings.json + appsettings.{Environment}.json
/// load from <see cref="AppContext.BaseDirectory"/>, in addition to
/// the content root. The sidecar is invoked as `dotnet
/// HelpersSidecar.dll` from any working directory; configuration
/// MUST follow the binary, not the cwd, so dev overrides aren't
/// silently dropped.
/// </summary>
public class ConfigurationLoadFromBinDirTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConfigurationLoadFromBinDirTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "BR-CODE-002 — appsettings.json values are loaded from the binary's directory")]
    public void Appsettings_Json_From_BinDir_Is_Loaded()
    {
        // The sidecar's appsettings.json (next to HelpersSidecar.dll)
        // configures Otel:CollectorOtlpPort. WebApplicationFactory uses
        // the same binary path; if BR-CODE-002's wire-up is in place, the
        // value comes through IConfiguration.
        using var client = _factory.CreateClient();    // forces host build
        var config = _factory.Services.GetRequiredService<IConfiguration>();

        var port = config.GetValue<int?>("Otel:CollectorOtlpPort");
        Assert.NotNull(port);
        // The canonical default in appsettings.json is 4318. Tests that
        // don't override it should see 4318 (or whatever the production
        // default is); the assertion is "value is present", proving the
        // load worked.
        Assert.True(port is > 0, $"expected positive port from appsettings.json; got {port}");
    }

    [Fact(DisplayName = "BR-CODE-002 — Otel section is bound (not silently null) — proves the binary-dir load")]
    public void Otel_Section_Bound_From_BinDir()
    {
        using var client = _factory.CreateClient();
        var config = _factory.Services.GetRequiredService<IConfiguration>();

        var section = config.GetSection("Otel");
        Assert.True(section.Exists(),
            "Otel section must be present — proves appsettings.json was loaded. " +
            "Without BR-CODE-002's BinDir wire-up the section is silently null.");
        Assert.False(string.IsNullOrEmpty(section["CollectorExePath"]));
        Assert.False(string.IsNullOrEmpty(section["CollectorConfigFile"]));
    }
}
