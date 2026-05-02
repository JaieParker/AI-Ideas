namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Invokes another skill's dispatch endpoint inside the same
/// helpers sidecar via HTTP loopback. Used by orchestrator
/// skills (currently only /demo) to chain into other skills
/// instead of bypassing them and calling the collector or
/// vendor APIs directly.
///
/// Production implementation routes through
/// <c>http://127.0.0.1:5050/skills/{name}/dispatch</c>; tests
/// substitute a recording fake.
/// </summary>
public interface ISkillDispatchClient
{
    /// <summary>
    /// POSTs to <c>/skills/{skillName}/dispatch</c> with the
    /// provided form parameters. Returns the dispatch endpoint's
    /// HTTP status code and body, or a non-200 result with the
    /// failure detail in <c>Body</c> when the loopback call
    /// itself fails (e.g. timeout, connection refused).
    /// </summary>
    Task<SkillDispatchResult> DispatchAsync(string skillName, IReadOnlyDictionary<string, string> form, CancellationToken ct = default);
}

public sealed record SkillDispatchResult(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
