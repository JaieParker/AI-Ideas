namespace HelpersSidecar.Domain;

/// <summary>
/// A curated authoritative external source a domain accepts as
/// citable. Trust is granted per-source, not per-host: adding a
/// new Martin Fowler article (or any new URL) requires its own
/// <see cref="TrustedReference"/> with its own <see cref="Why"/>.
///
/// The architecture-review agent (BR-PROCESS-009 / Plan-6) is the
/// primary consumer — its prompt includes the resolved domain's
/// <see cref="IDomain.TrustedReferences"/> and its response is
/// validated to cite only URLs that appear in the list.
/// </summary>
/// <remarks>BR-EXTEND-008 governs the curation discipline.</remarks>
public sealed record TrustedReference(
    string Title,
    Uri Url,
    string Why,
    DateOnly? AddedOn = null,
    string? AddedInPlan = null);
