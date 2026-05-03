namespace HelpersSidecar.Artefacts;

/// <summary>
/// The seam programmatic producers use to write durable
/// artefacts. Replaces direct <see cref="File.WriteAllText"/> /
/// <see cref="File.WriteAllBytes"/> calls in producer code so
/// the destination and the schema metadata flow through the
/// registry rather than being hardcoded at the call site.
/// </summary>
public interface IArtefactWriter
{
    Task<ArtefactWriteResult> WriteAsync(
        string artefactName,
        IReadOnlyDictionary<string, string> templateParams,
        ReadOnlyMemory<byte> body,
        CancellationToken ct = default);
}

public sealed record ArtefactWriteResult(
    string ArtefactName,
    string ResolvedKey,
    IReadOnlyList<DestinationWriteResult> DestinationResults,
    bool Success);

public sealed record DestinationWriteResult(
    string DestinationName,
    bool Success,
    string? Error);

public sealed class ArtefactWriter : IArtefactWriter
{
    private readonly IArtefactRegistry _registry;
    private readonly IReadOnlyDictionary<string, IArtefactDestination> _destinationsByName;

    public ArtefactWriter(IArtefactRegistry registry, IEnumerable<IArtefactDestination> destinations)
    {
        _registry = registry;
        _destinationsByName = destinations.ToDictionary(d => d.Name, StringComparer.Ordinal);
    }

    public async Task<ArtefactWriteResult> WriteAsync(
        string artefactName,
        IReadOnlyDictionary<string, string> templateParams,
        ReadOnlyMemory<byte> body,
        CancellationToken ct = default)
    {
        var spec = _registry.Get(artefactName);
        if (spec.Lifecycle == ArtefactLifecycle.UserEdited)
            throw new InvalidOperationException(
                $"BR-PROCESS-015: artefact '{artefactName}' is UserEdited; the sidecar must not write it.");

        var key = RenderKey(spec.KeyTemplate, templateParams);
        var metadata = new ArtefactMetadata(
            SchemaName:    spec.SchemaName,
            SchemaVersion: spec.SchemaVersion,
            CreatedAtUtc:  DateTimeOffset.UtcNow,
            ProducerName:  spec.Producer,
            Owner:         spec.Owner);

        var perDestination = new List<DestinationWriteResult>();
        var allRequiredOk = true;

        foreach (var destRef in spec.Destinations)
        {
            if (!_destinationsByName.TryGetValue(destRef.DestinationName, out var dest))
            {
                var msg = $"destination '{destRef.DestinationName}' not registered";
                perDestination.Add(new DestinationWriteResult(destRef.DestinationName, false, msg));
                if (destRef.OnFailure == DestinationFailureMode.Required) allRequiredOk = false;
                continue;
            }

            try
            {
                await dest.WriteAsync(key, body, metadata, ct);
                perDestination.Add(new DestinationWriteResult(destRef.DestinationName, true, null));
            }
            catch (Exception ex)
            {
                perDestination.Add(new DestinationWriteResult(destRef.DestinationName, false, ex.Message));
                if (destRef.OnFailure == DestinationFailureMode.Required) allRequiredOk = false;
            }
        }

        return new ArtefactWriteResult(artefactName, key, perDestination, allRequiredOk);
    }

    /// <summary>
    /// v1 templating: replaces <c>&lt;utc-ts&gt;</c> with the current
    /// UTC timestamp and any <c>&lt;param&gt;</c> with
    /// <paramref name="templateParams"/>'s entry. Unmapped segments
    /// are left as-is — callers see the literal in the resolved key
    /// and can spot the miss.
    /// </summary>
    private static string RenderKey(string template, IReadOnlyDictionary<string, string> templateParams)
    {
        var rendered = template.Replace("<utc-ts>",
            DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ"));
        foreach (var kv in templateParams)
            rendered = rendered.Replace($"<{kv.Key}>", kv.Value);
        return rendered;
    }
}
