using System.Text.RegularExpressions;

namespace HelpersSidecar.Domain;

/// <summary>
/// A normalised topic slug used in plan filenames and commit messages.
///
/// BR-EXTEND-005 — lowercase, alphanumeric + hyphens, ≤64 chars,
/// no leading/trailing hyphens, never empty.
/// </summary>
public sealed partial record TopicSlug
{
    public const int MaxLength = 64;

    public string Value { get; }

    private TopicSlug(string value) => Value = value;

    public override string ToString() => Value;

    /// <summary>
    /// Normalise <paramref name="raw"/> to a slug. Returns <see
    /// cref="SlugifyResult.Failure"/> when the input is null or
    /// normalises to an empty string.
    /// </summary>
    public static SlugifyResult TryCreate(string? raw)
    {
        if (raw is null)
            return SlugifyResult.Failure("input is null");

        var lowered = raw.ToLowerInvariant();
        var collapsed = NonAlphanumericRun().Replace(lowered, "-");
        var trimmed = collapsed.Trim('-');
        var truncated = trimmed.Length > MaxLength
            ? trimmed[..MaxLength].TrimEnd('-')
            : trimmed;

        if (string.IsNullOrEmpty(truncated))
            return SlugifyResult.Failure("input normalised to empty string");

        return SlugifyResult.Success(new TopicSlug(truncated));
    }

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumericRun();
}

/// <summary>
/// Outcome of <see cref="TopicSlug.TryCreate"/>. Either succeeds with a
/// slug or fails with a human-readable error.
/// </summary>
public readonly record struct SlugifyResult(bool Ok, TopicSlug? Slug, string? Error)
{
    public static SlugifyResult Success(TopicSlug slug) => new(true, slug, null);
    public static SlugifyResult Failure(string error) => new(false, null, error);
}
