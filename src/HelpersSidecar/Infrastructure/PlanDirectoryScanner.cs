namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Production implementation of <see cref="IPlanDirectoryScanner"/>.
/// Returns an empty list when the directory does not exist (callers
/// then treat that as "no plans yet" — see BR-EXTEND-004's empty case).
///
/// <para>Plan-9: scanner is prefix-agnostic. It returns every
/// <c>*.md</c> in the directory; callers filter via the resolved
/// domain's <see cref="HelpersSidecar.Domain.PlanFileConventions.TryParse"/>.
/// This lets one scanner serve any domain (OTEL's
/// <c>The-OTEL-Plan-*.md</c>, kai-platform's
/// <c>The-KaiPlatform-Plan-*.md</c>, etc.) without a per-domain
/// scanner instance.</para>
/// </summary>
public sealed class PlanDirectoryScanner : IPlanDirectoryScanner
{
    public IReadOnlyList<string> ListPlanFileNames(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return Array.Empty<string>();

        return Directory
            .EnumerateFiles(rootDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
    }
}
