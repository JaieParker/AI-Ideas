namespace HelpersSidecar.Application;

/// <summary>
/// Parsed result of an /enrich invocation. Pure data — no HTTP, no
/// IO. The endpoint takes one of these and dispatches.
/// </summary>
public sealed record EnrichVerb(EnrichVerbKind Kind, string? Key = null, string? Value = null)
{
    public static EnrichVerb Parse(string args)
    {
        var s = (args ?? string.Empty).Trim();

        if (s.Length == 0) return new EnrichVerb(EnrichVerbKind.Usage);

        if (s.StartsWith("--"))
        {
            // --show / --clear / --remove KEY
            var space = s.IndexOf(' ');
            var flag = space < 0 ? s : s[..space];
            var rest = space < 0 ? "" : s[(space + 1)..].Trim();

            return flag switch
            {
                "--show"   => new EnrichVerb(EnrichVerbKind.Show),
                "--clear"  => new EnrichVerb(EnrichVerbKind.Clear),
                "--remove" => rest.Length > 0
                                ? new EnrichVerb(EnrichVerbKind.Remove, Key: rest)
                                : new EnrichVerb(EnrichVerbKind.Usage),
                _ => new EnrichVerb(EnrichVerbKind.Usage),
            };
        }

        // key + value form. Value is everything after the first whitespace.
        var firstSpace = s.IndexOf(' ');
        if (firstSpace < 0) return new EnrichVerb(EnrichVerbKind.Usage);
        var key = s[..firstSpace];
        var value = s[(firstSpace + 1)..].Trim();
        if (value.Length == 0) return new EnrichVerb(EnrichVerbKind.Usage);
        return new EnrichVerb(EnrichVerbKind.Set, Key: key, Value: value);
    }
}

public enum EnrichVerbKind { Usage, Show, Clear, Remove, Set }
