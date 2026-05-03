namespace HelpersSidecar.Artefacts;

/// <summary>
/// Typed catalogue entry for one durable artefact this project
/// writes (or that the user maintains by hand). Registered in
/// DI by every producer that owns it; <see cref="IArtefactRegistry"/>
/// collects them all. Plan-11 (BR-PROCESS-015).
///
/// <para><see cref="KeyTemplate"/> is an informal string with
/// named segments enclosed in angle brackets, e.g.
/// <c>"demo-reports/&lt;utc-ts&gt;-&lt;domain&gt;.md"</c>. The
/// writer renders the segments at write time. v1 supports
/// <c>&lt;utc-ts&gt;</c> (ISO 8601, no separators) and any
/// caller-supplied parameters.</para>
/// </summary>
public sealed record ArtefactSpec(
    string Name,
    string KeyTemplate,
    IReadOnlyList<DestinationRef> Destinations,
    string SchemaName,
    int    SchemaVersion,
    ArtefactLifecycle Lifecycle,
    bool   GitTracked,
    string Producer,
    string GoverningBR,
    string? Owner,
    ArtefactCostClass CostClass);

public enum ArtefactLifecycle
{
    /// <summary>Written once per event; previous writes stay (demo report, ai-level report).</summary>
    OneShot,
    /// <summary>Grows over time; multiple writes append (telemetry.jsonl).</summary>
    AppendOnly,
    /// <summary>Each write supersedes the last (plans index, persistent-enrichments).</summary>
    Replaced,
    /// <summary>Transient process state (PID files, promote snapshots).</summary>
    RuntimeState,
    /// <summary>Hand-edited markdown; never written by the sidecar (BRs, retros, plans).</summary>
    UserEdited,
}

public enum ArtefactCostClass
{
    /// <summary>Local file; no measurable cost per write.</summary>
    Free,
    /// <summary>Each write costs (e.g. database insert).</summary>
    PerWrite,
    /// <summary>Per-write AND ongoing storage (e.g. S3 object).</summary>
    PerWriteAndStorage,
}

/// <summary>
/// References a destination by name plus the failure-mode the
/// writer applies if that destination errors.
/// </summary>
public sealed record DestinationRef(
    string DestinationName,
    DestinationFailureMode OnFailure);

public enum DestinationFailureMode
{
    /// <summary>Failure of this destination fails the whole write.</summary>
    Required,
    /// <summary>Failure is logged but doesn't fail the write.</summary>
    BestEffort,
}
