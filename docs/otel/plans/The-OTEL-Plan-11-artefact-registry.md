# Artefact registry — typed catalogue of every durable thing this project writes

> Plan-11 follows Plan-10 (`/ai-level`) which deliberately
> shipped an unregistered artefact (the AI-level report) as the
> forcing function for this plan. Plan-11 introduces
> `IArtefactRegistry`, retrofits ~10 existing producers through
> a typed `IArtefactWriter` seam, defines `BR-PROCESS-015` (every
> durable artefact is registered) and `BR-SECURITY-004` (remote
> destinations require explicit opt-in), and adds an `artefacts`
> projection slice to `/domain-info`.
>
> The `/artefacts` skill itself is deferred to Plan-12 to keep
> Plan-11 tight on machinery + retrofit. After Plan-11 lands,
> `/domain-info <owner> artefacts` already answers "what does
> this domain produce?"; Plan-12 adds the cross-cutting
> renderer.

## Motivation

Today the project writes durable artefacts to ~10 paths
scattered across the codebase. Each producer hard-codes its
output location, its schema marker, and its file-naming
convention. The conventions live across multiple BRs
(`BR-DEMO-004`, `BR-PROCESS-013`, `BR-PROCESS-011`) and plan
files. A new contributor asking "where does X get written?"
has no single source of truth. As the project grows
multi-domain (kai-platform incubating; Plan-9's per-domain
partitioning), the absence of a registry compounds — every new
artefact-producing feature has to re-decide and re-document its
output convention.

Three converging signals:

1. Plan-9's per-domain plan-file move surfaced the implicit
   convention as fragile under multi-domain.
2. Plan-10's `/ai-level` writer shipped without a registry
   entry — the second time we'd asked "where does this land?"
   without a structured answer.
3. The user's design conversation explicitly named the gap and
   pushed for an abstraction that supports remote destinations
   (S3, database, etc.) without shipping them yet.

A typed registry answers all three. Producers register their
spec in DI; a thin writer abstraction (`IArtefactWriter`) walks
the spec's destinations; cross-cutting queries become typed
data lookups.

## New / changed business rules

- **`BR-PROCESS-015` (new)** — Every durable artefact this
  project writes MUST be registered in `IArtefactRegistry`. The
  biconditional applies: a writer exists ⇔ a registry entry
  exists. Hand-edited artefacts (BRs, retros, incidents) are
  also registered for visibility but with `WriteMode:
  UserEdited` so they don't go through `IArtefactWriter`.

- **`BR-SECURITY-004` (new)** — Remote artefact destinations
  (S3, HTTP webhook, database, message queue, etc.) require
  explicit opt-in at TWO levels: (a) the destination must be
  declared in `appsettings.json` under `Artefacts:Destinations`
  AND enabled by an explicit startup flag; (b) per-artefact
  opt-in via `ArtefactSpec.Destinations` — enabling S3
  project-wide doesn't enable it for every artefact.
  Plan-11 ships zero remote destinations; the rule lands now
  so the security firewall is in place before any future plan
  proposes one.

- **`BR-PROCESS-013` extension** — The schema catalogue
  references the artefact registry as the source of truth. The
  rule's table becomes a snapshot rendered from the registry
  (or a future contributor regenerates it on amendment); the
  registry is canonical.

## The shape

### `ArtefactSpec`

```csharp
public sealed record ArtefactSpec(
    string Name,                              // "demo-report", "ai-level-report", "plans-index"
    string KeyTemplate,                       // "demo-reports/<utc-ts>-<domain>.md"
    IReadOnlyList<DestinationRef> Destinations,
    string SchemaName,                        // "DEMO_REPORT"
    int    SchemaVersion,                     // 1
    ArtefactLifecycle Lifecycle,              // OneShot | AppendOnly | Replaced | RuntimeState | UserEdited
    bool   GitTracked,
    string Producer,                          // typeof name of writer or "user"
    string GoverningBR,                       // "BR-DEMO-004"
    string? Owner,                            // "otel" | "cross-domain" | null (harness)
    ArtefactCostClass CostClass);             // Free | PerWrite | PerWriteAndStorage

public enum ArtefactLifecycle
{
    OneShot,        // Written once per event (demo report, ai-level report).
    AppendOnly,     // Grows over time (telemetry.jsonl).
    Replaced,       // Each write supersedes the last (plans index, persistent-enrichments).
    RuntimeState,   // Transient process state (PID files, promote snapshots).
    UserEdited,     // Hand-edited markdown (BRs, retros, incidents, plans).
}

public enum ArtefactCostClass
{
    Free,                  // Local file, no measurable cost.
    PerWrite,              // Each write costs (e.g. database insert).
    PerWriteAndStorage,    // Per-write AND ongoing storage (S3).
}

public sealed record DestinationRef(
    string DestinationName,                  // "local-fs", future "s3:audit-bucket"
    DestinationFailureMode OnFailure);       // Required | BestEffort
```

### `IArtefactDestination`

```csharp
public interface IArtefactDestination
{
    string Name { get; }
    DestinationKind Kind { get; }            // LocalFile | RemoteObjectStore | Database | Webhook
    Task WriteAsync(
        string key,
        ReadOnlyMemory<byte> body,
        ArtefactMetadata md,
        CancellationToken ct);
}

public sealed record ArtefactMetadata(
    string SchemaName,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string ProducerName,
    string? Owner);
```

`LocalFileDestination` is the only impl shipped in Plan-11.
Remote impls (`S3Destination`, `DatabaseDestination`, etc.) are
out of scope per `BR-SECURITY-004`'s opt-in default.

### `IArtefactRegistry`

```csharp
public interface IArtefactRegistry
{
    IReadOnlyList<ArtefactSpec> All { get; }
    ArtefactSpec Get(string name);
    bool TryGet(string name, out ArtefactSpec? spec);
    IReadOnlyList<ArtefactSpec> ByOwner(string? owner);
    IReadOnlyList<ArtefactSpec> ByLifecycle(ArtefactLifecycle l);
    IReadOnlyList<ArtefactSpec> BySchema(string schemaName);
}
```

DI: `AddSingleton<IArtefactRegistry, ArtefactRegistry>()`.
Producers register their `ArtefactSpec` via
`AddSingleton<ArtefactSpec>(...)` — same shape as `IDomain`
collection. The registry collects them at startup; a startup
self-check verifies every spec's `Producer` type resolves.

### `IArtefactWriter`

```csharp
public interface IArtefactWriter
{
    Task<ArtefactWriteResult> WriteAsync(
        string artefactName,
        IReadOnlyDictionary<string, string> templateParams,
        ReadOnlyMemory<byte> body,
        CancellationToken ct = default);
}
```

The writer:
1. Resolves the spec from the registry.
2. Renders `KeyTemplate` against `templateParams`.
3. For each `DestinationRef` in the spec, resolves the
   destination by name and calls `WriteAsync(key, body, md, ct)`.
4. Aggregates results; failures honour `OnFailure` per
   destination.

Producers move from `File.WriteAllText(absolutePath, body)` to
`writer.WriteAsync("demo-report", templateParams, body)`. The
absolute path stops appearing in producer code.

## Files affected

### New source

| Path | Change |
|---|---|
| `src/HelpersSidecar/Artefacts/ArtefactSpec.cs` | The record + enums. |
| `src/HelpersSidecar/Artefacts/IArtefactDestination.cs` | The interface + metadata + result types. |
| `src/HelpersSidecar/Artefacts/LocalFileDestination.cs` | Single shipped destination. Resolves keys against project-root + `output/` or `docs/` per spec convention. |
| `src/HelpersSidecar/Artefacts/IArtefactRegistry.cs` | Interface. |
| `src/HelpersSidecar/Artefacts/ArtefactRegistry.cs` | DI-collected impl. Mirrors `DomainResolver` shape. |
| `src/HelpersSidecar/Artefacts/IArtefactWriter.cs` | Interface. |
| `src/HelpersSidecar/Artefacts/ArtefactWriter.cs` | Impl. Resolves spec, renders template, walks destinations. |
| `src/HelpersSidecar/Artefacts/ArtefactSpecs.cs` | Static factory: every project artefact's spec, registered in DI from one place. |

### Modified source (retrofit)

| Path | Change |
|---|---|
| `src/HelpersSidecar/Domain/MarkdownDemoReportWriter.cs` | Constructor takes `IArtefactWriter`; replaces `File.WriteAllText` calls. |
| `src/HelpersSidecar/Application/AiLevelReportWriter.cs` | Same retrofit. |
| `src/HelpersSidecar/Endpoints/AiLevelDispatchEndpoint.cs` | Producer call uses `IArtefactWriter.WriteAsync("ai-level-report", ...)` instead of inline `File.WriteAllText`. |
| `src/HelpersSidecar/Endpoints/PlansIndexEndpoint.cs` | Same retrofit for `WriteTo` mode. |
| `src/HelpersSidecar/Endpoints/DomainInfoDispatchEndpoint.cs` | New `artefacts` projection slice — calls `registry.ByOwner(domain.Name)`. |
| `src/HelpersSidecar/Program.cs` | Register `IArtefactRegistry`, `IArtefactWriter`, `LocalFileDestination`, every `ArtefactSpec`. |

### New tests

| Path | Coverage |
|---|---|
| `tests/HelpersSidecar.Tests/Artefacts/ArtefactRegistryTests.cs` | Registration, lookup by name/owner/lifecycle/schema, duplicate detection. |
| `tests/HelpersSidecar.Tests/Artefacts/LocalFileDestinationTests.cs` | Write to relative key resolves under project root; missing parent dirs created; idempotent. |
| `tests/HelpersSidecar.Tests/Artefacts/ArtefactWriterTests.cs` | Template rendering, destination iteration, `OnFailure: Required` propagates, `OnFailure: BestEffort` swallows. |
| `tests/HelpersSidecar.Tests/Artefacts/ArtefactSpecsTests.cs` | BR-PROCESS-015 biconditional: every registered spec's `Producer` type exists; every schema mentioned in `BR-PROCESS-013`'s catalogue table has a registry entry. |

### Modified tests

| Path | Change |
|---|---|
| `tests/HelpersSidecar.Tests/Endpoints/DomainInfoDispatchTests.cs` | New test: `artefacts` projection slice resolves through registry. |
| `tests/HelpersSidecar.Tests/Application/AiLevelReportWriterTests.cs` | Constructor signature change accommodated; verify the writer routes through `IArtefactWriter`. |

### Modified docs

| Path | Change |
|---|---|
| `docs/business-rules.md` | Add `BR-PROCESS-015` and `BR-SECURITY-004`. Amend `BR-PROCESS-013` to reference the registry as canonical. |
| `CLAUDE.md` | "Architecture summary" section gains the artefact-registry layer. |

## Existing artefacts that get registered

Plan-11 registers ~10 `ArtefactSpec`s up front. The retrofit IS
the proof the abstraction is right — if any registration is
awkward, the design is wrong and we iterate before locking in.

| Name | Owner | Lifecycle | Producer | BR | Schema |
|---|---|---|---|---|---|
| demo-report | otel | OneShot | `MarkdownDemoReportWriter` | BR-DEMO-004 | DEMO_REPORT v1 |
| ai-level-report | cross-domain | OneShot | `AiLevelReportWriter` | BR-SKILL-013 | AI_LEVEL_REPORT v1 |
| plans-index | cross-domain | Replaced | `PlansIndexBuilder` | BR-PROCESS-013 | PLAN_INDEX v1 |
| telemetry | otel | AppendOnly | (collector — file-exporter) | BR-OTEL-001 | (OTLP JSON) |
| persistent-enrichments | otel | Replaced | (collector — control endpoint) | BR-ENRICH-005 | (JSON state) |
| sidecar-pid | null (harness) | RuntimeState | `Program.cs` lifetime hooks | BR-PROCESS-008 | (raw int) |
| collector-pid | null (harness) | RuntimeState | `ProcessLifecycle` | BR-PROCESS-008 | (raw int) |
| sidecar-promote-snapshot | null (harness) | RuntimeState | `ProcessLifecycle.PromoteAsync` | BR-PROCESS-012 | (binary copy) |
| business-rules | cross-domain | UserEdited | (user) | (self-referential) | (markdown) |
| process-incidents | cross-domain | UserEdited | (user) | (self-referential) | (markdown) |
| retros | cross-domain | UserEdited | (user) | BR-PROCESS-002 | (markdown) |
| plan-files | otel | UserEdited | (user via `/extend-skills`) | BR-EXTEND-004 | (markdown) |

12 total. Producer column distinguishes programmatic vs user-edited.
The collector-side artefacts (telemetry, persistent-enrichments)
are registered for visibility even though the Go collector writes
them — the registry is the project's catalogue, not just the
sidecar's.

## Test approach

The biconditional `BR-PROCESS-015` is enforced by a single test:

```csharp
[Fact(DisplayName = "BR-PROCESS-015 — every Producer in ArtefactSpecs resolves to a real type")]
public void Every_Producer_Resolves()
{
    foreach (var spec in registry.All)
    {
        if (spec.Lifecycle == ArtefactLifecycle.UserEdited) continue;
        var type = Type.GetType(spec.Producer);
        Assert.NotNull(type);
    }
}
```

A second test enforces the other side of the biconditional —
every schema named in `BR-PROCESS-013`'s catalogue has a registry
entry:

```csharp
[Fact(DisplayName = "BR-PROCESS-013 — every catalogue schema has a registry entry")]
public void Catalogue_Schemas_Are_Registered()
{
    var catalogueSchemas = new[] { "DEMO_REPORT", "AI_LEVEL_REPORT", "PLAN_INDEX",
        "PROMOTE_REPORT", "ARCHITECTURE_REVIEW" };
    foreach (var schema in catalogueSchemas)
    {
        var matches = registry.BySchema(schema);
        Assert.NotEmpty(matches);
    }
}
```

## Phase ordering

1. **Phase 1 (this commit)** — plan file. `plan:` prefix.
2. **Phase 1.5** — `/architecture-review` against the plan;
   resolve any `EXTENDS` markers.
3. **Phase 2a** — `Artefacts/` namespace: spec + destination
   interface + local impl + registry + writer + tests. No
   retrofit yet. `feat(otel):` prefix.
4. **Phase 2b** — Retrofit existing producers
   (`MarkdownDemoReportWriter`, `AiLevelReportWriter`,
   `AiLevelDispatchEndpoint`, `PlansIndexEndpoint`). Register
   all 12 specs in `ArtefactSpecs`. Update affected tests.
   `feat(otel):` prefix.
5. **Phase 2c** — `/domain-info` `artefacts` projection slice.
   Test added to `DomainInfoDispatchTests`. `feat(otel):`
   prefix.
6. **Phase 2d** — `BR-PROCESS-015` + `BR-SECURITY-004` text in
   `docs/business-rules.md`. `BR-PROCESS-013` amendment.
   CLAUDE.md update. `feat(otel):` prefix.
7. **Phase 3 — Build.** `chore:` if artefacts changed.
8. **Phase 4 — Test.** `test:` prefix.

## Rollback

Standard `git revert <commit>` per phase. The retrofit is the
heaviest phase; a revert restores `File.WriteAllText` calls.
The registry phase reverts cleanly because no other code
depends on `IArtefactRegistry` until Phase 2c.

## Out of scope

- **`/artefacts` skill.** Plan-12. The skill is a thin renderer
  over the registry; once Plan-11 lands, `/domain-info <owner>
  artefacts` already exposes the per-owner view. Plan-12 adds
  the cross-cutting query surface (`/artefacts
  --lifecycle=runtime-state`, etc.).
- **Remote destinations.** Plan-13+ (only when there's a real
  motivator — compliance archival, cross-project audit, DR).
  `BR-SECURITY-004` ships now to gate future shipping.
- **Schema migration tooling.** `v1 → v2` schema bumps are still
  manual edits to producers and the catalogue; automating the
  migration is a future plan.
- **`PROMOTE_REPORT v1` and `ARCHITECTURE_REVIEW v1` writers.**
  Both are `BR-PROCESS-013`-listed but neither has a writer
  shipped yet (Plan-7's promote ships outcomes via the CLI;
  Plan-6's architecture-review responses are emitted by Claude
  in-session). The registry entries reflect this — `Producer`
  field is `(deferred)` and `Lifecycle: OneShot` with a note.

## Architecture review decisions

> BR-PROCESS-009 gate. `/architecture-review` runs against this
> plan in Phase 1.5; resolutions land here.

_(Awaiting Phase 1.5 — markers + resolutions land here.)_

## What kai-platform inherits

When `KaiPlatformDomain` lands, every artefact it produces
registers with `Owner: "kai-platform"`. `/domain-info
kai-platform artefacts` returns the right list automatically. No
registry-side changes needed for new domains — the existing
contract handles them.
