namespace HelpersSidecar.Application;

/// <summary>
/// Parsed result of an /extend-skills invocation. Pure data — no IO.
/// The dispatch endpoint takes one of these and gathers the
/// deterministic state Claude needs to begin the gated flow.
///
/// Args shape: <code>&lt;domain&gt; [&lt;topic&gt; | revert | status]</code>
///
/// The first token is the domain name (required). Remaining tokens
/// are either a sub-command (revert / status) or a free-form topic
/// for the begin verb.
/// </summary>
public sealed record ExtendSkillsVerb(ExtendSkillsVerbKind Kind, string Domain, string? Topic = null)
{
    public static ExtendSkillsVerb Parse(string args)
    {
        var s = (args ?? string.Empty).Trim();
        if (s.Length == 0)
            return new ExtendSkillsVerb(ExtendSkillsVerbKind.UsageMissingDomain, string.Empty);

        // First token is the domain.
        var firstSpace = s.IndexOf(' ');
        var domain = firstSpace < 0 ? s : s[..firstSpace];
        var rest = firstSpace < 0 ? string.Empty : s[(firstSpace + 1)..].Trim();

        if (rest.Length == 0)
            return new ExtendSkillsVerb(ExtendSkillsVerbKind.Begin, domain, Topic: null);

        // Second token is either a sub-command or the start of a topic.
        var secondSpace = rest.IndexOf(' ');
        var second = secondSpace < 0 ? rest : rest[..secondSpace];

        return second switch
        {
            "revert" => new ExtendSkillsVerb(ExtendSkillsVerbKind.Revert, domain),
            "status" => new ExtendSkillsVerb(ExtendSkillsVerbKind.Status, domain),
            _        => new ExtendSkillsVerb(ExtendSkillsVerbKind.Begin, domain, Topic: rest),
        };
    }
}

public enum ExtendSkillsVerbKind
{
    UsageMissingDomain,    // empty args — domain is required
    Begin,                 // <domain> or <domain> <topic> — start the multi-phase flow
    Revert,                // <domain> revert — list recent extend-flow commits, ask how far to revert
    Status,                // <domain> status — report which phase a current flow is at (best-effort)
}
