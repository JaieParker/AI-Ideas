namespace HelpersSidecar.Domain;

/// <summary>
/// A parsed plan filename — number + optional slug — scoped to a
/// specific domain via <see cref="Conventions"/>.
///
/// Backs BR-EXTEND-004 (consecutive numbering, no gaps) and is
/// domain-parameterised so multiple domains can coexist with
/// distinct prefixes (e.g. <c>The-OTEL-Plan-N-...</c> vs the
/// future kai-platform's own pattern).
/// </summary>
public sealed record PlanFileName(int Number, string? Slug, PlanFileConventions Conventions)
{
    public string FileName => Conventions.FileNameFor(Number, Slug);

    /// <summary>
    /// Parse a filename into its <see cref="Number"/> and optional
    /// <see cref="Slug"/> components against the supplied
    /// <paramref name="conventions"/>, or return <c>null</c> when
    /// the filename doesn't match.
    /// </summary>
    public static PlanFileName? TryParse(string? fileName, PlanFileConventions conventions)
    {
        if (fileName is null) return null;
        if (!conventions.TryParse(fileName, out var number, out var slug)) return null;
        return new PlanFileName(number, slug, conventions);
    }
}
