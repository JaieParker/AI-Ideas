namespace HelpersSidecar.Domain;

/// <summary>
/// The OTEL domain's <see cref="IDemoTarget"/>. Plan-23 retired
/// the in-process <c>ISkillDispatchClient</c> loopback chain in
/// favour of agent-turn chaining via the Claude Code Skill tool —
/// the <see cref="DemoCase.Plan"/> below returns step descriptors
/// only; the demo's SKILL.md body invokes each via Skill so the
/// harness emits <c>claude_code.skill_activated</c> events for
/// every chained step (BR-DEMO-002 amended).
///
/// Steps 9 and 13 are <see cref="DemoStepDescriptor.Kind"/>=
/// "observe" file reads against output/telemetry.jsonl — read-
/// only verification of records the upstream chain produced.
/// </summary>
public sealed class OtelDomainDemo : IDemoTarget
{
    public const string TelemetryFile = "output/telemetry.jsonl";

    public string TargetName => "otel";
    public string TargetKind => "domain";

    public IReadOnlyList<DemoCase> Demos { get; } = new[]
    {
        new DemoCase(
            Name:        "happy-path",
            Description: "Bring the collector up, configure persistent + per-session enrichments, run /weather working + failing under two ticket ids, observe JSONL records, tear down.",
            IsDefault:   true,
            Plan:        BuildHappyPathPlan),
    };

    private static Task<IReadOnlyList<DemoStepDescriptor>> BuildHappyPathPlan(
        DemoContext ctx, CancellationToken ct)
    {
        IReadOnlyList<DemoStepDescriptor> steps = new[]
        {
            new DemoStepDescriptor(1,  "otel",    "up",
                "/otel up — bring collector up (idempotent)",
                Expect: "collector"),
            new DemoStepDescriptor(2,  "otel",    "set user:Jaie",
                "/otel set user:Jaie — persistent enrichment",
                Expect: "user"),
            new DemoStepDescriptor(3,  "otel",    "set workstation:LightningBlue",
                "/otel set workstation:LightningBlue — persistent enrichment"),
            new DemoStepDescriptor(4,  "otel",    "set version:0.001",
                "/otel set version:0.001 — persistent enrichment"),
            new DemoStepDescriptor(5,  "otel",    "get user",
                "/otel get user — round-trip read of an earlier set",
                Expect: "Jaie"),
            new DemoStepDescriptor(6,  "enrich",  "ticket.id JA-0001",
                "/enrich ticket.id JA-0001 — per-session enrichment"),
            new DemoStepDescriptor(7,  "weather", "London",
                "/weather London — working request"),
            new DemoStepDescriptor(8,  "weather", "$(rm -rf /)",
                "/weather $(rm -rf /) — adversarial input, expect graceful failure"),
            new DemoStepDescriptor(9,  string.Empty, string.Empty,
                $"observe {TelemetryFile} — after JA-0001, before JA-0002",
                Kind: "observe",
                ObserveTarget: TelemetryFile),
            new DemoStepDescriptor(10, "enrich",  "ticket.id JA-0002",
                "/enrich ticket.id JA-0002 — per-session enrichment swap"),
            new DemoStepDescriptor(11, "weather", "London",
                "/weather London — same call, new ticket"),
            new DemoStepDescriptor(12, "weather", "$(rm -rf /)",
                "/weather $(rm -rf /) — adversarial input, expect graceful failure"),
            new DemoStepDescriptor(13, string.Empty, string.Empty,
                $"observe {TelemetryFile} — after JA-0002 set",
                Kind: "observe",
                ObserveTarget: TelemetryFile),
            new DemoStepDescriptor(14, "otel",    "down",
                "/otel down — full lifecycle complete; system fully reversible",
                Expect: "stopped"),
        };
        return Task.FromResult(steps);
    }
}
