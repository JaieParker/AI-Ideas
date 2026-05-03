namespace HelpersSidecar.Artefacts;

/// <summary>
/// One place to register every project artefact's spec. Plan-11
/// (BR-PROCESS-015): every durable artefact must appear here.
/// Producers are NOT named via <see cref="Type.AssemblyQualifiedName"/>
/// because that couples the spec to the assembly version; the
/// <see cref="ArtefactSpec.Producer"/> field carries the simple
/// type name and the biconditional test resolves it through
/// <c>Type.GetType(...)</c> against the loaded assemblies.
/// </summary>
public static class ArtefactSpecs
{
    private static readonly IReadOnlyList<DestinationRef> LocalRequired = new[]
    {
        new DestinationRef("local-fs", DestinationFailureMode.Required),
    };

    public static IReadOnlyList<ArtefactSpec> All { get; } = new[]
    {
        // -------- programmatic, OTEL-owned --------
        new ArtefactSpec(
            Name: "demo-report",
            KeyTemplate: "output/demo-reports/<utc-ts>-<domain>.md",
            Destinations: LocalRequired,
            SchemaName: "DEMO_REPORT", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.OneShot,
            GitTracked: false,
            Producer: "HelpersSidecar.Domain.MarkdownDemoReportWriter",
            GoverningBR: "BR-DEMO-004",
            Owner: "otel",
            CostClass: ArtefactCostClass.Free),

        new ArtefactSpec(
            Name: "telemetry",
            KeyTemplate: "output/telemetry.jsonl",
            Destinations: LocalRequired,
            SchemaName: "OTLP", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.AppendOnly,
            GitTracked: false,
            Producer: "(collector — file-exporter)",
            GoverningBR: "BR-OTEL-001",
            Owner: "otel",
            CostClass: ArtefactCostClass.Free),

        new ArtefactSpec(
            Name: "persistent-enrichments",
            KeyTemplate: "persistent-enrichments.json",
            Destinations: LocalRequired,
            SchemaName: "PERSISTENT_ENRICHMENTS", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.Replaced,
            GitTracked: false,
            Producer: "(collector — control endpoint)",
            GoverningBR: "BR-ENRICH-005",
            Owner: "otel",
            CostClass: ArtefactCostClass.Free),

        // -------- programmatic, cross-domain --------
        new ArtefactSpec(
            Name: "ai-level-report",
            KeyTemplate: "output/ai-level/<utc-ts>-<scope>.md",
            Destinations: LocalRequired,
            SchemaName: "AI_LEVEL_REPORT", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.OneShot,
            GitTracked: false,
            Producer: "HelpersSidecar.Application.AiLevelReportWriter",
            GoverningBR: "BR-SKILL-013",
            Owner: "cross-domain",
            CostClass: ArtefactCostClass.Free),

        new ArtefactSpec(
            Name: "plans-index",
            KeyTemplate: "docs/INDEX.md",
            Destinations: LocalRequired,
            SchemaName: "PLAN_INDEX", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.Replaced,
            GitTracked: true,
            Producer: "HelpersSidecar.Application.PlansIndexBuilder",
            GoverningBR: "BR-PROCESS-013",
            Owner: "cross-domain",
            CostClass: ArtefactCostClass.Free),

        // -------- harness-level (no domain) --------
        new ArtefactSpec(
            Name: "sidecar-pid",
            KeyTemplate: ".claude/runtime/sidecar.pid",
            Destinations: LocalRequired,
            SchemaName: "PID_FILE", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.RuntimeState,
            GitTracked: false,
            Producer: "HelpersSidecar.Program (lifetime hooks)",
            GoverningBR: "BR-PROCESS-008",
            Owner: null,
            CostClass: ArtefactCostClass.Free),

        new ArtefactSpec(
            Name: "collector-pid",
            KeyTemplate: ".claude/runtime/collector.pid",
            Destinations: LocalRequired,
            SchemaName: "PID_FILE", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.RuntimeState,
            GitTracked: false,
            Producer: "HelpersSidecar.Infrastructure.ProcessLifecycle",
            GoverningBR: "BR-PROCESS-008",
            Owner: null,
            CostClass: ArtefactCostClass.Free),

        new ArtefactSpec(
            Name: "sidecar-promote-snapshot",
            KeyTemplate: "src/HelpersSidecar/bin/Debug.bak/",
            Destinations: LocalRequired,
            SchemaName: "PROMOTE_SNAPSHOT", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.RuntimeState,
            GitTracked: false,
            Producer: "HelpersSidecar.Infrastructure.ProcessLifecycle",
            GoverningBR: "BR-PROCESS-012",
            Owner: null,
            CostClass: ArtefactCostClass.Free),

        // -------- user-edited (registered for visibility) --------
        new ArtefactSpec(
            Name: "business-rules",
            KeyTemplate: "docs/business-rules.md",
            Destinations: Array.Empty<DestinationRef>(),
            SchemaName: "BUSINESS_RULES", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.UserEdited,
            GitTracked: true,
            Producer: "(user)",
            GoverningBR: "(self-referential)",
            Owner: "cross-domain",
            CostClass: ArtefactCostClass.Free),

        new ArtefactSpec(
            Name: "process-incidents",
            KeyTemplate: "docs/process-incidents.md",
            Destinations: Array.Empty<DestinationRef>(),
            SchemaName: "PROCESS_INCIDENTS", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.UserEdited,
            GitTracked: true,
            Producer: "(user)",
            GoverningBR: "(self-referential)",
            Owner: "cross-domain",
            CostClass: ArtefactCostClass.Free),

        new ArtefactSpec(
            Name: "retros",
            KeyTemplate: "docs/retros.md",
            Destinations: Array.Empty<DestinationRef>(),
            SchemaName: "RETROS", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.UserEdited,
            GitTracked: true,
            Producer: "(user)",
            GoverningBR: "BR-PROCESS-002",
            Owner: "cross-domain",
            CostClass: ArtefactCostClass.Free),

        new ArtefactSpec(
            Name: "plan-files",
            KeyTemplate: "docs/<domain>/plans/The-OTEL-Plan-<n>-<slug>.md",
            Destinations: Array.Empty<DestinationRef>(),
            SchemaName: "PLAN_FILE", SchemaVersion: 1,
            Lifecycle: ArtefactLifecycle.UserEdited,
            GitTracked: true,
            Producer: "(user via /extend-skills)",
            GoverningBR: "BR-EXTEND-004",
            Owner: "otel",
            CostClass: ArtefactCostClass.Free),
    };
}
