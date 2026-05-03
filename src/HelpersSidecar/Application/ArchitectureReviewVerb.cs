namespace HelpersSidecar.Application;

/// <summary>
/// Parsed result of an /architecture-review invocation.
///
/// Args shape: <code>&lt;target&gt; [--domain=&lt;name&gt;]</code>
///
/// <see cref="Target"/> identifies what to review:
///   - a path relative to the project root (e.g. <c>The-OTEL-Plan-7-...md</c>),
///   - a plan id like <c>plan-7</c>,
///   - the literal <c>current</c> for the uncommitted diff,
///   - a git branch name.
///
/// <see cref="Domain"/> defaults to <c>otel</c> if absent. The
/// dispatch endpoint resolves domain via <see cref="HelpersSidecar.Infrastructure.IDomainResolver"/>;
/// unknown domain returns a structured error.
///
/// Backs BR-PROCESS-009 (the human-decision gate) and
/// BR-SKILL-012 (the qualitative-judgement Shape B contract).
/// </summary>
public sealed record ArchitectureReviewVerb(string Target, string? Domain = null)
{
    public static ArchitectureReviewVerb Parse(string args)
    {
        var s = (args ?? string.Empty).Trim();
        if (s.Length == 0)
            return new ArchitectureReviewVerb(Target: string.Empty);

        // Split on whitespace; the first non-flag token is the target;
        // any --domain=<name> token specifies the domain override.
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? target = null;
        string? domain = null;

        foreach (var token in tokens)
        {
            if (token.StartsWith("--domain=", StringComparison.Ordinal))
            {
                domain = token["--domain=".Length..];
            }
            else if (target is null)
            {
                target = token;
            }
        }

        return new ArchitectureReviewVerb(
            Target: target ?? string.Empty,
            Domain: string.IsNullOrEmpty(domain) ? null : domain);
    }

    public bool HasTarget => !string.IsNullOrEmpty(Target);
}
