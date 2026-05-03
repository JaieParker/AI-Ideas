namespace HelpersSidecar.Artefacts;

/// <summary>
/// Catalogue of every <see cref="ArtefactSpec"/> registered with
/// DI. Mirrors <see cref="HelpersSidecar.Infrastructure.IDomainResolver"/>'s
/// shape — wraps an <see cref="IEnumerable{ArtefactSpec}"/> and
/// exposes name-based + filtered queries.
/// </summary>
public interface IArtefactRegistry
{
    IReadOnlyList<ArtefactSpec> All { get; }
    ArtefactSpec Get(string name);
    bool TryGet(string name, out ArtefactSpec? spec);
    IReadOnlyList<ArtefactSpec> ByOwner(string? owner);
    IReadOnlyList<ArtefactSpec> ByLifecycle(ArtefactLifecycle lifecycle);
    IReadOnlyList<ArtefactSpec> BySchema(string schemaName);
}

public sealed class ArtefactRegistry : IArtefactRegistry
{
    private readonly IReadOnlyDictionary<string, ArtefactSpec> _byName;

    public IReadOnlyList<ArtefactSpec> All { get; }

    public ArtefactRegistry(IEnumerable<ArtefactSpec> specs)
    {
        var list = specs.ToList();
        var byName = new Dictionary<string, ArtefactSpec>(StringComparer.Ordinal);
        foreach (var s in list)
        {
            if (byName.ContainsKey(s.Name))
                throw new InvalidOperationException(
                    $"BR-PROCESS-015: duplicate ArtefactSpec.Name '{s.Name}'. " +
                    "Each registered artefact must have a unique name.");
            byName.Add(s.Name, s);
        }
        _byName = byName;
        All = list;
    }

    public ArtefactSpec Get(string name) =>
        _byName.TryGetValue(name, out var s)
            ? s
            : throw new KeyNotFoundException(
                $"unknown artefact '{name}' (known: {string.Join(", ", _byName.Keys)})");

    public bool TryGet(string name, out ArtefactSpec? spec)
    {
        var ok = _byName.TryGetValue(name, out var found);
        spec = found;
        return ok;
    }

    public IReadOnlyList<ArtefactSpec> ByOwner(string? owner) =>
        All.Where(s => s.Owner == owner).ToList();

    public IReadOnlyList<ArtefactSpec> ByLifecycle(ArtefactLifecycle lifecycle) =>
        All.Where(s => s.Lifecycle == lifecycle).ToList();

    public IReadOnlyList<ArtefactSpec> BySchema(string schemaName) =>
        All.Where(s => string.Equals(s.SchemaName, schemaName, StringComparison.Ordinal)).ToList();
}
