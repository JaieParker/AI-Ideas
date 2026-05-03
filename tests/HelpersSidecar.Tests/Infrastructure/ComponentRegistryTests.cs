using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Infrastructure;

/// <summary>
/// BR-PROCESS-011 / BR-CODE-004 — ComponentRegistry.Default
/// configures the green sidecar to bind its StagingPort, not the
/// appsettings-baked Listener:Port. ASP.NET command-line config
/// overrides win over file-based config; the green spawn args
/// MUST carry --Listener:Port=&lt;StagingPort&gt; so blue keeps
/// owning :5050 while green binds :5051 simultaneously.
/// </summary>
public class ComponentRegistryTests
{
    [Fact(DisplayName = "BR-CODE-004 — green sidecar SpawnArgs override Listener:Port to StagingPort (Plan-9)")]
    public void Green_Spawn_Overrides_ListenerPort_To_StagingPort()
    {
        var registry = ComponentRegistry.Default(
            sidecarPort: 5050,
            sidecarExe: "irrelevant.dll",
            runtimeDir: ".claude/runtime",
            sidecarStagingPort: 5051);

        var spec = registry.Get("sidecar");
        Assert.NotNull(spec.Staging);
        var staging = spec.Staging!;

        Assert.Equal(5051, staging.StagingPort);
        Assert.Contains("--Listener:Port=5051", staging.SpawnArgs);
    }

    [Fact(DisplayName = "BR-CODE-004 — when StagingPort is changed the SpawnArgs override tracks it")]
    public void Green_Spawn_ListenerPort_Override_Tracks_StagingPort()
    {
        var registry = ComponentRegistry.Default(
            sidecarPort: 5050,
            sidecarExe: "irrelevant.dll",
            runtimeDir: ".claude/runtime",
            sidecarStagingPort: 6000);

        var spec = registry.Get("sidecar");
        Assert.Contains("--Listener:Port=6000", spec.Staging!.SpawnArgs);
        Assert.DoesNotContain("--Listener:Port=5051", spec.Staging.SpawnArgs);
    }

    [Fact(DisplayName = "BR-PROCESS-011 — green spawn launches the staging dll explicitly")]
    public void Green_Spawn_Targets_Staging_Dll()
    {
        var registry = ComponentRegistry.Default(
            sidecarPort: 5050,
            sidecarExe: "irrelevant.dll",
            runtimeDir: ".claude/runtime",
            sidecarStagingPort: 5051);

        var staging = registry.Get("sidecar").Staging!;
        Assert.Equal("dotnet", staging.SpawnCommand);
        Assert.Contains(staging.SpawnArgs, a => a.EndsWith("HelpersSidecar.dll", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "BR-PROCESS-011 — when no staging port is provided, sidecar.Staging is null")]
    public void No_Staging_Port_Means_No_Staging_Spec()
    {
        var registry = ComponentRegistry.Default(
            sidecarPort: 5050,
            sidecarExe: "irrelevant.dll",
            runtimeDir: ".claude/runtime",
            sidecarStagingPort: null);

        Assert.Null(registry.Get("sidecar").Staging);
    }
}
