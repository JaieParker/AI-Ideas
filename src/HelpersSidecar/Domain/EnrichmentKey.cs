using System.Text.RegularExpressions;

namespace HelpersSidecar.Domain;

/// <summary>
/// An OTEL attribute key used as an enrichment identifier.
///
/// BR-ENRICH-001 — must match <c>^[a-z][a-z0-9_.\-]*$</c> and be
/// ≤ 64 characters. Constructable only via <see cref="TryCreate"/>;
/// once an instance exists, the invariant holds.
/// </summary>
public sealed partial record EnrichmentKey
{
    public const int MaxLength = 64;

    public string Value { get; }

    private EnrichmentKey(string value) => Value = value;

    public override string ToString() => Value;

    public static KeyValidationResult TryCreate(string? raw)
    {
        if (raw is null)
            return KeyValidationResult.Failure("key is null");
        if (raw.Length == 0)
            return KeyValidationResult.Failure("key is empty");
        if (raw.Length > MaxLength)
            return KeyValidationResult.Failure(
                $"key length {raw.Length} exceeds {MaxLength}");
        if (!KeyPattern().IsMatch(raw))
            return KeyValidationResult.Failure(
                @"key must match ^[a-z][a-z0-9_.\-]*$");

        return KeyValidationResult.Success(new EnrichmentKey(raw));
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_.\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

public readonly record struct KeyValidationResult(bool Ok, EnrichmentKey? Key, string? Error)
{
    public static KeyValidationResult Success(EnrichmentKey key) => new(true, key, null);
    public static KeyValidationResult Failure(string error) => new(false, null, error);
}
