namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Per-component staging configuration (BR-PROCESS-011). When a
/// <see cref="ComponentSpec"/> opts in by carrying a non-null
/// <c>Staging</c> property, that component supports the
/// stage/promote/discard lifecycle on top of the basic
/// start/stop/sweep that every component has.
///
/// <see cref="StagingPort"/> MUST differ from the component's
/// production <see cref="ComponentSpec.Port"/> so blue and green
/// can coexist. <see cref="StagingPath"/> is the directory
/// <see cref="BuildCommand"/> writes to (e.g.
/// <c>src/HelpersSidecar/bin/Staging/net10.0/</c>).
/// <see cref="StagingExePath"/> is what gets spawned for green
/// — typically a DLL inside <see cref="StagingPath"/>.
/// </summary>
public sealed record StagingSpec(
    int StagingPort,
    string StagingPath,
    string StagingPidFile,
    string BuildCommand,
    IReadOnlyList<string> BuildArgs,
    string SpawnCommand,
    IReadOnlyList<string> SpawnArgs);
