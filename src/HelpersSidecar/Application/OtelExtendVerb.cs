namespace HelpersSidecar.Application;

/// <summary>
/// Parsed result of an /otel-extend invocation. Pure data — no IO.
/// The dispatch endpoint takes one of these and gathers the
/// deterministic state Claude needs to begin the gated flow.
/// </summary>
public sealed record OtelExtendVerb(OtelExtendVerbKind Kind, string? Topic = null)
{
    public static OtelExtendVerb Parse(string args)
    {
        var s = (args ?? string.Empty).Trim();
        if (s.Length == 0) return new OtelExtendVerb(OtelExtendVerbKind.Begin);

        // First token is either a sub-command (revert / status) or
        // the start of a free-form topic.
        var firstSpace = s.IndexOf(' ');
        var first = firstSpace < 0 ? s : s[..firstSpace];
        var rest = firstSpace < 0 ? "" : s[(firstSpace + 1)..].Trim();

        return first switch
        {
            "revert" => new OtelExtendVerb(OtelExtendVerbKind.Revert),
            "status" => new OtelExtendVerb(OtelExtendVerbKind.Status),
            _        => new OtelExtendVerb(OtelExtendVerbKind.Begin, Topic: s),
        };
    }
}

public enum OtelExtendVerbKind
{
    Begin,    // empty args or a topic — start the multi-phase flow
    Revert,   // list recent extend-flow commits, ask how far to revert
    Status,   // report which phase a current flow is at (best-effort)
}
