namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Registry of long-running components the project owns. Each
/// component has a stable name (used in PID file paths and CLI
/// args), a port it binds, an exe path, and the args used to
/// launch it.
///
/// v1 contains only the helpers sidecar. The collector tier
/// joins this registry once Plan-5 (the .NET-only collector
/// pivot) lands; until then it's managed outside our lifecycle
/// service.
/// </summary>
public sealed record ComponentSpec(
    string Name,
    int Port,
    string PidFile,
    string ExePath,
    IReadOnlyList<string> Args,
    StagingSpec? Staging = null);

public interface IComponentRegistry
{
    ComponentSpec Get(string name);
    bool TryGet(string name, out ComponentSpec? spec);
    IReadOnlyList<string> Names { get; }
}

public sealed class ComponentRegistry : IComponentRegistry
{
    private readonly IReadOnlyDictionary<string, ComponentSpec> _components;

    public ComponentRegistry(IReadOnlyDictionary<string, ComponentSpec> components)
    {
        _components = components;
    }

    public ComponentSpec Get(string name) =>
        _components.TryGetValue(name, out var spec)
            ? spec
            : throw new KeyNotFoundException($"unknown component '{name}' (known: {string.Join(", ", _components.Keys)})");

    public bool TryGet(string name, out ComponentSpec? spec)
    {
        var ok = _components.TryGetValue(name, out var found);
        spec = found;
        return ok;
    }

    public IReadOnlyList<string> Names => _components.Keys.ToList();

    /// <summary>
    /// Default registry — sidecar (with optional staging) + collector.
    /// The collector tier joins the registry once <c>/otel up</c>
    /// exists (BR-OTEL-006). Path and args for the collector come
    /// from the host process's working dir + the configured collector
    /// config file (defaults to <c>config.yaml</c>).
    ///
    /// When <paramref name="sidecarStagingPort"/> is non-null, the
    /// sidecar component carries a <see cref="StagingSpec"/> for
    /// stage/promote/discard (BR-PROCESS-011 / 012). Build target is
    /// <c>src/HelpersSidecar/bin/Staging/net10.0</c>; green spawn is
    /// <c>dotnet &lt;path&gt;/HelpersSidecar.dll</c>.
    /// </summary>
    public static ComponentRegistry Default(int sidecarPort, string sidecarExe, string runtimeDir,
        string? collectorExe = null, string? collectorConfigFile = null,
        int? sidecarStagingPort = null)
    {
        StagingSpec? sidecarStaging = null;
        if (sidecarStagingPort is int sp)
        {
            var stagingPath = Path.Combine("src", "HelpersSidecar", "bin", "Staging", "net10.0");
            var stagingDll  = Path.Combine(stagingPath, "HelpersSidecar.dll");
            sidecarStaging = new StagingSpec(
                StagingPort:    sp,
                StagingPath:    stagingPath,
                StagingPidFile: Path.Combine(runtimeDir, "sidecar-green.pid"),
                BuildCommand:   "dotnet",
                BuildArgs:      new[] { "build", Path.Combine("src", "HelpersSidecar", "HelpersSidecar.csproj"),
                                        "-c", "Debug", "-o", stagingPath },
                SpawnCommand:   "dotnet",
                // BR-CODE-004: green sidecar must bind StagingPort, not the
                // appsettings-baked Listener:Port. ASP.NET command-line config
                // overrides win over file-based config; --Listener:Port=<n>
                // points the green instance at :StagingPort while blue keeps
                // serving on its appsettings-baked Listener:Port.
                SpawnArgs:      new[] { stagingDll, $"--Listener:Port={sp}" });
        }

        var dict = new Dictionary<string, ComponentSpec>(StringComparer.Ordinal)
        {
            ["sidecar"] = new ComponentSpec(
                Name: "sidecar",
                Port: sidecarPort,
                PidFile: Path.Combine(runtimeDir, "sidecar.pid"),
                ExePath: sidecarExe,
                Args: Array.Empty<string>(),
                Staging: sidecarStaging),
        };

        if (!string.IsNullOrEmpty(collectorExe))
        {
            dict["collector"] = new ComponentSpec(
                Name: "collector",
                Port: 13133,             // collector control API
                PidFile: Path.Combine(runtimeDir, "collector.pid"),
                ExePath: collectorExe,
                Args: string.IsNullOrEmpty(collectorConfigFile)
                    ? new[] { "--config=config.yaml" }
                    : new[] { $"--config={collectorConfigFile}" });
        }

        return new(dict);
    }
}
