using System.Text.RegularExpressions;

namespace HelpersSidecar.Domain;

/// <summary>
/// A parsed plan filename of the form
/// <c>The-OTEL-Plan(-{N}(-{slug})?)?\.md</c>.
///
/// Backs BR-EXTEND-004 (consecutive numbering, no gaps, base file
/// is implicitly N=1).
/// </summary>
public sealed partial record PlanFileName(int Number, string? Slug)
{
    public string FileName =>
        Number == 1 && Slug is null ? "The-OTEL-Plan.md"
        : Slug is null              ? $"The-OTEL-Plan-{Number}.md"
                                    : $"The-OTEL-Plan-{Number}-{Slug}.md";

    /// <summary>
    /// Parse a filename into its <see cref="Number"/> and optional
    /// <see cref="Slug"/> components, or return <c>null</c> when the
    /// filename doesn't match the plan-file pattern.
    /// </summary>
    public static PlanFileName? TryParse(string? fileName)
    {
        if (fileName is null) return null;
        var m = Pattern().Match(fileName);
        if (!m.Success) return null;

        if (!m.Groups[1].Success)
            return new PlanFileName(1, null);   // base file

        var number = int.Parse(m.Groups[1].Value);
        var slug = m.Groups[2].Success ? m.Groups[2].Value : null;
        return new PlanFileName(number, slug);
    }

    [GeneratedRegex(
        @"^The-OTEL-Plan(?:-(\d+)(?:-([a-z0-9](?:[a-z0-9-]*[a-z0-9])?))?)?\.md$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
