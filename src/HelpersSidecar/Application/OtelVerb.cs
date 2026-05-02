namespace HelpersSidecar.Application;

/// <summary>
/// Parsed result of an /otel invocation. Pure data — no HTTP, no IO.
/// </summary>
public sealed record OtelVerb(OtelVerbKind Kind, string? Key = null, string? Value = null, string? Topic = null, string[]? Keys = null, string? ConfigFile = null)
{
    public static OtelVerb Parse(string args)
    {
        var s = (args ?? string.Empty).Trim();
        if (s.Length == 0) return new OtelVerb(OtelVerbKind.Setup);

        // First token is the verb.
        var firstSpace = s.IndexOf(' ');
        var verb = firstSpace < 0 ? s : s[..firstSpace];
        var rest = firstSpace < 0 ? "" : s[(firstSpace + 1)..].Trim();

        return verb switch
        {
            "on"      => new OtelVerb(OtelVerbKind.On),
            "off"     => new OtelVerb(OtelVerbKind.Off),
            "up"      => new OtelVerb(OtelVerbKind.Up,
                                       ConfigFile: rest.Length > 0 ? rest : null),
            "down"    => new OtelVerb(OtelVerbKind.Down),
            "status"  => new OtelVerb(OtelVerbKind.Status),
            "restart" => new OtelVerb(OtelVerbKind.Restart),
            "help"    => new OtelVerb(OtelVerbKind.Help),
            "extend"  => new OtelVerb(OtelVerbKind.Extend, Topic: rest.Length > 0 ? rest : null),
            "set"     => ParseSet(rest),
            "unset"   => rest.Length > 0
                            ? new OtelVerb(OtelVerbKind.Unset, Key: rest)
                            : new OtelVerb(OtelVerbKind.Usage),
            "get"     => ParseGet(rest),
            "config"  => rest == "clear"
                            ? new OtelVerb(OtelVerbKind.ConfigClear)
                            : (rest.Length == 0
                                ? new OtelVerb(OtelVerbKind.Config)
                                : new OtelVerb(OtelVerbKind.Usage)),
            _         => new OtelVerb(OtelVerbKind.Usage),
        };
    }

    private static OtelVerb ParseSet(string rest)
    {
        // /otel set <key>:<value>
        var colon = rest.IndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1) return new OtelVerb(OtelVerbKind.Usage);
        var key = rest[..colon].Trim();
        var value = rest[(colon + 1)..].Trim();
        if (key.Length == 0 || value.Length == 0) return new OtelVerb(OtelVerbKind.Usage);
        return new OtelVerb(OtelVerbKind.Set, Key: key, Value: value);
    }

    private static OtelVerb ParseGet(string rest)
    {
        if (rest.Length == 0) return new OtelVerb(OtelVerbKind.Usage);
        var keys = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (keys.Length == 1) return new OtelVerb(OtelVerbKind.Get, Key: keys[0]);
        return new OtelVerb(OtelVerbKind.GetMany, Keys: keys);
    }
}

public enum OtelVerbKind
{
    Usage,
    Setup,        // empty args — bootstrap-and-status
    On, Off, Status, Restart, Help,
    Up, Down,     // collector-tier lifecycle (BR-OTEL-006)
    Set, Unset, Get, GetMany,
    Config, ConfigClear,
    Extend,       // emits the chain marker
}
