using System.Text.RegularExpressions;

namespace HelpersSidecar.Domain;

/// <summary>
/// An OTEL attribute value used as an enrichment.
///
/// BR-ENRICH-002 — length ≤ 4096 chars; longer values are rejected.
/// BR-ENRICH-003 — values matching obvious-secret prefixes attract
/// a non-blocking warning (the value is still accepted; the caller
/// gets a chance to think twice).
/// </summary>
public sealed partial record EnrichmentValue
{
    public const int MaxLength = 4096;

    public string Value { get; }

    private EnrichmentValue(string value) => Value = value;

    public override string ToString() => Value;

    public static ValueValidationResult TryCreate(string? raw)
    {
        if (raw is null)
            return ValueValidationResult.Failure("value is null");
        if (raw.Length > MaxLength)
            return ValueValidationResult.Failure(
                $"value length {raw.Length} exceeds {MaxLength}");

        var warnings = new List<string>();
        if (raw.Length > 0 && SecretPrefix().IsMatch(raw))
            warnings.Add("value matches an obvious-secret prefix " +
                "(AKIA / ghp_ / gho_ / ghu_ / ghs_ / ghr_ / sk- / xoxb-) — " +
                "telemetry is not a place to store credentials");

        return ValueValidationResult.Success(new EnrichmentValue(raw), warnings);
    }

    /// <summary>BR-ENRICH-003 — secret-shaped prefixes that warrant a warning.</summary>
    [GeneratedRegex(@"^(AKIA|ghp_|gho_|ghu_|ghs_|ghr_|sk-|xoxb-)", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPrefix();
}

public readonly record struct ValueValidationResult(
    bool Ok,
    EnrichmentValue? Value,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public static ValueValidationResult Success(EnrichmentValue value, IReadOnlyList<string> warnings)
        => new(true, value, warnings, null);

    public static ValueValidationResult Failure(string error)
        => new(false, null, Array.Empty<string>(), error);
}
