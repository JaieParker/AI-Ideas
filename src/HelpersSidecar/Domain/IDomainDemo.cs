namespace HelpersSidecar.Domain;

/// <summary>
/// A target the /demo skill can invoke. Domains register one
/// (or more, in future plans) of these; individual skills will
/// register their own in a later plan. The contract is data-only:
/// <see cref="DemoCase.Plan"/> returns a list of step descriptors;
/// the live agent-turn body of /demo executes each step via the
/// Claude Code Skill tool, producing real
/// <c>claude_code.skill_activated</c> events per BR-DEMO-002.
/// </summary>
public interface IDemoTarget
{
    /// <summary>Argument the user types after /demo (e.g. "otel").</summary>
    string TargetName { get; }

    /// <summary>"domain" or "skill" — drives /demo's resolution + reporting.</summary>
    string TargetKind { get; }

    /// <summary>
    /// All registered demo cases for this target. Exactly one
    /// must have <see cref="DemoCase.IsDefault"/> = true. Additional
    /// cases land in future plans (Plan-23 ships one default per
    /// target).
    /// </summary>
    IReadOnlyList<DemoCase> Demos { get; }
}

/// <summary>
/// A single named demo flow on a target. The
/// <see cref="Plan"/> returns its 1..N steps as data; no execution
/// inside the sidecar.
/// </summary>
public sealed record DemoCase(
    string Name,
    string Description,
    bool IsDefault,
    Func<DemoContext, CancellationToken, Task<IReadOnlyList<DemoStepDescriptor>>> Plan);

/// <summary>
/// What a demo case's <see cref="DemoCase.Plan"/> needs from the
/// platform. Today: just the session id; future per-skill demos
/// may need more (kept as a record for forward compatibility).
/// </summary>
public sealed record DemoContext(string SessionId);

/// <summary>
/// One step in a demo plan — a request to invoke a skill via the
/// Claude Code Skill tool with the given args, expecting a marker
/// in the response that proves the step's behaviour. Pure data.
/// </summary>
/// <param name="Number">Ordinal in the plan, 1-based.</param>
/// <param name="Skill">Skill name as the user would type after /,
///   e.g. "otel" / "enrich" / "weather".</param>
/// <param name="Args">Args to pass — sent to the chained skill
///   exactly as a user would type them after the slash.</param>
/// <param name="Label">One-line human-readable description for
///   the demo report.</param>
/// <param name="Expect">Optional marker text the agent should
///   look for in the chained skill's response. Empty when no
///   programmatic check is meaningful (the agent still records
///   the response for the report).</param>
/// <param name="Kind">"invoke" for skill chains; "observe" for
///   read-only file-system observations baked into the plan
///   (e.g. count records in output/telemetry.jsonl).</param>
/// <param name="ObserveTarget">For Kind="observe", the file or
///   resource to inspect. Empty otherwise.</param>
public sealed record DemoStepDescriptor(
    int Number,
    string Skill,
    string Args,
    string Label,
    string Expect = "",
    string Kind = "invoke",
    string ObserveTarget = "");

/// <summary>
/// Per-step result reported back from the live /demo run via
/// POST /skills/demo/observe. Accumulates into a DEMO_REPORT v1
/// per BR-DEMO-004 once every step has been observed.
/// </summary>
public sealed record DemoStepResult(
    int Number,
    string Label,
    bool Pass,
    string Detail,
    DateTimeOffset StartedAt = default,
    DateTimeOffset EndedAt = default);
