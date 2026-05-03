namespace HelpersSidecar.Artefacts;

/// <summary>
/// The only destination shipped in Plan-11. Resolves the
/// artefact's key against the project root (current working
/// directory) and writes the body via <see cref="File.WriteAllBytes"/>.
/// Creates parent directories as needed.
///
/// <para>Per BR-SECURITY-001 / 004 this is the default-allowed
/// destination — local writes don't expand the trust boundary.
/// All other destination kinds are remote and require the
/// opt-in machinery in BR-SECURITY-004.</para>
/// </summary>
public sealed class LocalFileDestination : IArtefactDestination
{
    private readonly string _projectRoot;

    public LocalFileDestination()
        : this(Directory.GetCurrentDirectory()) { }

    /// <summary>For tests — pass an absolute root so writes don't depend on cwd.</summary>
    public LocalFileDestination(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    public string Name => "local-fs";
    public DestinationKind Kind => DestinationKind.LocalFile;

    public Task WriteAsync(
        string key,
        ReadOnlyMemory<byte> body,
        ArtefactMetadata metadata,
        CancellationToken ct = default)
    {
        var path = Path.IsPathRooted(key) ? key : Path.Combine(_projectRoot, key);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(path, body.ToArray());
        return Task.CompletedTask;
    }
}
