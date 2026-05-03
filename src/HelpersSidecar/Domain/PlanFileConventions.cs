using System.Text.RegularExpressions;

namespace HelpersSidecar.Domain;

/// <summary>
/// How a domain names its plan files. The convention is a
/// <see cref="Prefix"/> followed by an optional <c>-{n}</c> and an
/// optional <c>-{slug}</c>, all suffixed with <c>.md</c>:
///
/// <code>
///   {Prefix}.md                         (base file when n=NumberFloor and no slug)
///   {Prefix}-{n}.md                     (no-slug case)
///   {Prefix}-{n}-{slug}.md              (canonical multi-plan case)
/// </code>
///
/// For OTEL: <c>Prefix = "The-OTEL-Plan"</c>, so the canonical
/// filename for plan 7 with slug <c>"domain-interface"</c> is
/// <c>The-OTEL-Plan-7-domain-interface.md</c>.
/// </summary>
public sealed record PlanFileConventions(string Prefix, int NumberFloor = 1)
{
    public string Suffix => ".md";

    /// <summary>Compose a filename from a number + optional slug.</summary>
    public string FileNameFor(int number, string? slug = null) =>
        number == NumberFloor && string.IsNullOrEmpty(slug)
            ? $"{Prefix}{Suffix}"
            : string.IsNullOrEmpty(slug)
                ? $"{Prefix}-{number}{Suffix}"
                : $"{Prefix}-{number}-{slug}{Suffix}";

    /// <summary>
    /// Parse a filename matching this convention into its number +
    /// optional slug. Returns <c>true</c> when the filename matches.
    /// On match for the base file (no number visible in the filename),
    /// <paramref name="number"/> is set to <see cref="NumberFloor"/>.
    /// </summary>
    public bool TryParse(string fileName, out int number, out string? slug)
    {
        number = 0;
        slug = null;
        if (string.IsNullOrEmpty(fileName)) return false;
        var pattern = $"^{Regex.Escape(Prefix)}(?:-(\\d+)(?:-([a-z0-9](?:[a-z0-9-]*[a-z0-9])?))?)?{Regex.Escape(Suffix)}$";
        var m = Regex.Match(fileName, pattern, RegexOptions.CultureInvariant);
        if (!m.Success) return false;
        if (!m.Groups[1].Success)
        {
            number = NumberFloor;
            return true;
        }
        number = int.Parse(m.Groups[1].Value);
        if (m.Groups[2].Success) slug = m.Groups[2].Value;
        return true;
    }
}
