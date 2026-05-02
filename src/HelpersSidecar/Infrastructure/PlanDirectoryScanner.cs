namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Production implementation of <see cref="IPlanDirectoryScanner"/>.
/// Returns an empty list when the directory does not exist (callers
/// then treat that as "no plans yet" — see BR-EXTEND-004's empty case).
/// </summary>
public sealed class PlanDirectoryScanner : IPlanDirectoryScanner
{
    public IReadOnlyList<string> ListPlanFileNames(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return Array.Empty<string>();

        return Directory
            .EnumerateFiles(rootDirectory, "The-OTEL-Plan*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
    }
}
