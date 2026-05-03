using HelpersSidecar.Domain;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Name-based lookup over the registered <see cref="IDomain"/>
/// implementations. Wraps <c>IEnumerable&lt;IDomain&gt;</c> from DI;
/// every domain-aware skill consumes this seam (BR-PROCESS-007 —
/// tests mock here so no test fans out across domains).
/// </summary>
public interface IDomainResolver
{
    IDomain ResolveOrThrow(string name);
    bool TryResolve(string name, out IDomain? domain);
    IReadOnlyList<string> KnownNames { get; }
    IReadOnlyList<IDomain> All { get; }
}

public sealed class DomainResolver : IDomainResolver
{
    private readonly IReadOnlyDictionary<string, IDomain> _byName;
    public IReadOnlyList<IDomain> All { get; }

    public DomainResolver(IEnumerable<IDomain> domains)
    {
        var list = domains.ToList();
        var byName = new Dictionary<string, IDomain>(StringComparer.Ordinal);
        foreach (var d in list)
        {
            if (byName.ContainsKey(d.Name))
                throw new InvalidOperationException(
                    $"BR-EXTEND-006: duplicate domain Name '{d.Name}'. " +
                    "Each IDomain implementation must have a unique Name.");
            byName.Add(d.Name, d);
        }
        _byName = byName;
        All = list;
    }

    public IDomain ResolveOrThrow(string name) =>
        _byName.TryGetValue(name, out var d)
            ? d
            : throw new KeyNotFoundException(
                $"unknown domain '{name}' (known: {string.Join(", ", _byName.Keys)})");

    public bool TryResolve(string name, out IDomain? domain)
    {
        var ok = _byName.TryGetValue(name, out var found);
        domain = found;
        return ok;
    }

    public IReadOnlyList<string> KnownNames =>
        _byName.Keys.OrderBy(s => s, StringComparer.Ordinal).ToList();
}
