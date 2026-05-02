namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Lists plan-shaped filenames (<c>The-OTEL-Plan*.md</c>) directly
/// under a project root. Returned strings are bare filenames, not
/// full paths. Tests substitute a fake; production wires
/// <see cref="PlanDirectoryScanner"/>.
/// </summary>
public interface IPlanDirectoryScanner
{
    IReadOnlyList<string> ListPlanFileNames(string rootDirectory);
}
