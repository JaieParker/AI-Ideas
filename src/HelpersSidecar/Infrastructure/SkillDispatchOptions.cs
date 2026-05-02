namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Settings for the loopback skill-dispatch HTTP client. Bound
/// from the <c>SkillDispatch</c> section of appsettings.json.
/// Concrete defaults live in appsettings.json (BR-CODE-001 — no
/// magic URLs in code).
/// </summary>
public sealed class SkillDispatchOptions
{
    public const string SectionName = "SkillDispatch";

    public string BaseUrl { get; set; } = "http://127.0.0.1:5050";
    public int TimeoutSeconds { get; set; } = 10;
}
