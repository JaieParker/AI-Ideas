using HelpersSidecar.Domain;

namespace HelpersSidecar.Application;

/// <summary>
/// BR-EXTEND-004 — given the set of plan filenames currently on
/// disk, decide what the next plan file should be called.
///
/// Pure function. The filesystem scan that produces
/// <paramref name="existingFiles"/> lives in Infrastructure so this
/// can be unit-tested without IO.
/// </summary>
public static class NextPlanFileName
{
    public static PlanFileName Compute(IEnumerable<string> existingFiles, string? slug = null)
    {
        var existingNumbers = existingFiles
            .Select(PlanFileName.TryParse)
            .Where(p => p is not null)
            .Select(p => p!.Number)
            .ToList();

        // No plan files at all → return the base file. The slug is
        // ignored in this case because the base file doesn't carry
        // one; callers should set up the base before extending.
        if (existingNumbers.Count == 0)
            return new PlanFileName(1, null);

        var next = existingNumbers.Max() + 1;
        return new PlanFileName(next, slug);
    }
}
