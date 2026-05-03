using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Application;

/// <summary>
/// Resolves the set of domains impacted by a list of changed
/// file paths. Drives BR-EXTEND-011 — domain-scoped integration
/// testing. Inputs are project-root-relative paths (typically
/// from <c>git diff --name-only</c>); outputs are the names of
/// every domain whose <see cref="IDomain.GovernedGlobs"/> or
/// <see cref="IDomain.PlanFiles.Directory"/> intersects any
/// changed path.
///
/// <para>If any path is identified as cross-domain (lives under
/// <c>docs/cross-domain/</c>, modifies a cross-domain BR file,
/// or doesn't match any domain's globs), the result includes
/// every known domain name — i.e. cross-domain changes
/// conservatively trigger every domain's integration tests.</para>
/// </summary>
public sealed class DomainImpactScope
{
    private readonly IDomainResolver _resolver;

    private static readonly string[] CrossDomainPathPrefixes =
    {
        "docs/cross-domain/",
        "docs/business-rules.md",
        "docs/process-incidents.md",
        "CLAUDE.md",
    };

    public DomainImpactScope(IDomainResolver resolver) => _resolver = resolver;

    public DomainImpactScopeResult ResolveImpactedDomains(IReadOnlyList<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(changedPaths);

        var impacted = new HashSet<string>(StringComparer.Ordinal);
        var crossDomainHit = false;

        foreach (var raw in changedPaths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var path = raw.Replace('\\', '/');

            if (IsCrossDomain(path))
            {
                crossDomainHit = true;
                continue;
            }

            var matchedAnyDomain = false;
            foreach (var domain in _resolver.All)
            {
                if (PathBelongsToDomain(path, domain))
                {
                    impacted.Add(domain.Name);
                    matchedAnyDomain = true;
                }
            }

            // A change that doesn't live under any domain's
            // governed globs / plan dir AND isn't recognised as
            // cross-domain is treated as cross-domain
            // conservatively — better to over-test than to miss
            // a regression.
            if (!matchedAnyDomain)
                crossDomainHit = true;
        }

        if (crossDomainHit)
        {
            foreach (var domain in _resolver.All)
                impacted.Add(domain.Name);
        }

        return new DomainImpactScopeResult(
            ImpactedDomains: impacted.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            CrossDomainTriggered: crossDomainHit);
    }

    private static bool IsCrossDomain(string path)
    {
        foreach (var prefix in CrossDomainPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool PathBelongsToDomain(string path, IDomain domain)
    {
        var planDir = domain.PlanFiles.Directory;
        if (!string.IsNullOrEmpty(planDir) && planDir != "." &&
            path.StartsWith(planDir + "/", StringComparison.Ordinal))
            return true;

        foreach (var glob in domain.GovernedGlobs)
        {
            if (GlobMatches(glob, path)) return true;
        }

        return false;
    }

    private static bool GlobMatches(string glob, string path)
    {
        var normalised = glob.Replace('\\', '/');

        var doubleStar = normalised.IndexOf("**", StringComparison.Ordinal);
        if (doubleStar >= 0)
        {
            var prefix = normalised[..doubleStar].TrimEnd('/');
            if (string.IsNullOrEmpty(prefix)) return true;
            return path.StartsWith(prefix + "/", StringComparison.Ordinal) ||
                   path.Equals(prefix, StringComparison.Ordinal);
        }

        var star = normalised.IndexOf('*');
        if (star >= 0)
        {
            var prefix = normalised[..star];
            var suffix = normalised[(star + 1)..];
            return path.StartsWith(prefix, StringComparison.Ordinal) &&
                   path.EndsWith(suffix, StringComparison.Ordinal) &&
                   path.Length >= prefix.Length + suffix.Length;
        }

        return path.Equals(normalised, StringComparison.Ordinal);
    }
}

/// <summary>
/// Result of <see cref="DomainImpactScope.ResolveImpactedDomains"/>.
/// <see cref="ImpactedDomains"/> is sorted, distinct.
/// <see cref="CrossDomainTriggered"/> is true when any changed
/// path was identified as cross-domain (or didn't match any
/// domain) — in that case <see cref="ImpactedDomains"/> contains
/// every registered domain's name.
/// </summary>
public sealed record DomainImpactScopeResult(
    IReadOnlyList<string> ImpactedDomains,
    bool CrossDomainTriggered);
