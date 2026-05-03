namespace HelpersSidecar.Artefacts;

/// <summary>
/// One destination an artefact's body can be written to. Plan-11
/// ships only <see cref="LocalFileDestination"/>; remote
/// destinations (S3, database, webhook) are deferred behind the
/// two-level opt-in machinery defined in BR-SECURITY-004.
/// </summary>
public interface IArtefactDestination
{
    /// <summary>
    /// Stable name referenced by <see cref="DestinationRef.DestinationName"/>.
    /// Local destination is "local-fs"; future destinations follow
    /// the pattern "kind:identifier" (e.g. "s3:audit-bucket").
    /// </summary>
    string Name { get; }

    DestinationKind Kind { get; }

    Task WriteAsync(
        string key,
        ReadOnlyMemory<byte> body,
        ArtefactMetadata metadata,
        CancellationToken ct = default);
}

public enum DestinationKind
{
    LocalFile,
    RemoteObjectStore,
    Database,
    Webhook,
}

/// <summary>
/// Carried alongside every write so destinations that support
/// per-object metadata (S3 user-metadata, database columns) can
/// surface the schema name + version + producer.
/// </summary>
public sealed record ArtefactMetadata(
    string SchemaName,
    int    SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string ProducerName,
    string? Owner);
