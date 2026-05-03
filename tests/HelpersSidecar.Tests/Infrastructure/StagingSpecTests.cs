using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Infrastructure;

/// <summary>
/// BR-PROCESS-011 — StagingSpec value-object discipline. The
/// implementation does NOT validate at construction — these
/// tests document the invariants ProcessLifecycle relies on
/// (Phase 2b will enforce them at the use-site).
/// </summary>
public class StagingSpecTests
{
    [Fact(DisplayName = "BR-PROCESS-011 — StagingSpec is constructible with all fields populated")]
    public void StagingSpec_Can_Construct()
    {
        var spec = new StagingSpec(
            StagingPort: 5051,
            StagingPath: "src/HelpersSidecar/bin/Staging/net10.0",
            StagingPidFile: ".claude/runtime/sidecar-green.pid",
            BuildCommand: "dotnet",
            BuildArgs: new[] { "build", "src/HelpersSidecar", "-c", "Debug" },
            SpawnCommand: "dotnet",
            SpawnArgs: new[] { "src/HelpersSidecar/bin/Staging/net10.0/HelpersSidecar.dll" });

        Assert.Equal(5051, spec.StagingPort);
        Assert.Equal("dotnet", spec.BuildCommand);
        Assert.Equal(4, spec.BuildArgs.Count);
        Assert.Equal("dotnet", spec.SpawnCommand);
        Assert.Single(spec.SpawnArgs);
    }

    [Fact(DisplayName = "BR-PROCESS-011 — ComponentSpec.Staging is optional (default null)")]
    public void ComponentSpec_Staging_Optional()
    {
        var spec = new ComponentSpec(
            Name: "sidecar",
            Port: 5050,
            PidFile: ".claude/runtime/sidecar.pid",
            ExePath: "src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll",
            Args: Array.Empty<string>());
        Assert.Null(spec.Staging);
    }

    [Fact(DisplayName = "BR-PROCESS-011 — ComponentSpec carries a StagingSpec when one is provided")]
    public void ComponentSpec_Carries_StagingSpec()
    {
        var staging = new StagingSpec(
            StagingPort: 5051,
            StagingPath: "x",
            StagingPidFile: ".claude/runtime/sidecar-green.pid",
            BuildCommand: "dotnet",
            BuildArgs: new[] { "build" },
            SpawnCommand: "dotnet",
            SpawnArgs: new[] { "x/HelpersSidecar.dll" });
        var spec = new ComponentSpec(
            Name: "sidecar",
            Port: 5050,
            PidFile: ".claude/runtime/sidecar.pid",
            ExePath: "x/HelpersSidecar.dll",
            Args: Array.Empty<string>(),
            Staging: staging);
        Assert.NotNull(spec.Staging);
        Assert.Equal(5051, spec.Staging!.StagingPort);
    }

    [Fact(DisplayName = "BR-PROCESS-011 — StageOutcome enum names cover the documented state-machine cells")]
    public void StageOutcome_Has_Documented_Cells()
    {
        var values = Enum.GetValues<StageOutcome>().Select(v => v.ToString()).ToHashSet();
        Assert.Contains(nameof(StageOutcome.Staged), values);
        Assert.Contains(nameof(StageOutcome.BuildFailed), values);
        Assert.Contains(nameof(StageOutcome.AlreadyStaged), values);
        Assert.Contains(nameof(StageOutcome.NotStageable), values);
        Assert.Contains(nameof(StageOutcome.HealthCheckFailed), values);
    }

    [Fact(DisplayName = "BR-PROCESS-012 — PromoteOutcome enum covers the rollback contract")]
    public void PromoteOutcome_Has_Rollback_Contract()
    {
        var values = Enum.GetValues<PromoteOutcome>().Select(v => v.ToString()).ToHashSet();
        Assert.Contains(nameof(PromoteOutcome.Promoted), values);
        Assert.Contains(nameof(PromoteOutcome.RolledBack), values);
        Assert.Contains(nameof(PromoteOutcome.NoGreenStaged), values);
        Assert.Contains(nameof(PromoteOutcome.GreenNotHealthy), values);
        Assert.Contains(nameof(PromoteOutcome.NotStageable), values);
    }
}
