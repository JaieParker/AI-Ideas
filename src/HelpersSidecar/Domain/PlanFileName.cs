namespace HelpersSidecar.Domain;

/// <summary>
/// A parsed plan filename — number + optional slug — scoped to a
/// specific domain via <see cref="Conventions"/>.
///
/// Backs BR-EXTEND-004 (consecutive numbering, no gaps) and is
/// domain-parameterised so multiple domains can coexist with
/// distinct prefixes (e.g. <c>The-OTEL-Plan-N-...</c> vs the
/// future kai-platform's own pattern).
///
/// Phase 2b transitional shape: backward-compat constructors and
/// parsers default to OTEL conventions. The defaults are removed
/// in Phase 2c when the rename + explicit domain wiring lands.
/// </summary>
public sealed record PlanFileName(int Number, string? Slug, PlanFileConventions Conventions)
{
    /// <summary>OTEL conventions — used by the Phase 2b backward-compat overloads.</summary>
    private static readonly PlanFileConventions OtelDefault = new("The-OTEL-Plan");

    /// <summary>
    /// Backward-compat constructor: assumes OTEL conventions.
    /// To be removed in Plan-5 Phase 2c.
    /// </summary>
    public PlanFileName(int number, string? slug)
        : this(number, slug, OtelDefault) { }

    public string FileName => Conventions.FileNameFor(Number, Slug);

    /// <summary>Backward-compat parser: assumes OTEL conventions.</summary>
    public static PlanFileName? TryParse(string? fileName) =>
        TryParse(fileName, OtelDefault);

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
