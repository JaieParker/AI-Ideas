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
    IReadOnlyList<string> Args);

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

    /// <summary>Default registry — sidecar only in v1.</summary>
    public static ComponentRegistry Default(int sidecarPort, string sidecarExe, string runtimeDir) =>
        new(new Dictionary<string, ComponentSpec>(StringComparer.Ordinal)
        {
            ["sidecar"] = new ComponentSpec(
                Name: "sidecar",
                Port: sidecarPort,
                PidFile: Path.Combine(runtimeDir, "sidecar.pid"),
                ExePath: sidecarExe,
                Args: Array.Empty<string>()),
        });
}
