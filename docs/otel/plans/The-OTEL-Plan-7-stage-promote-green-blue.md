# Stage / promote / discard — green-blue zero-downtime rebuilds for the helpers sidecar

> Plan-7 follows Plan-5 (`8ede7fb`–`7cba5ae` — domain interface +
> rename + IDomainDemo + /domain-info) and Plan-6 (`c2b0ce3` —
> drafted, unimplemented). Plan-7 closes the OTEL-continuity gap
> the user surfaced during Plan-5 implementation: every sidecar
> rebuild today requires `Stop-Process` on `:5050`, the build,
> then a restart — OTEL is off during the gap, session-scoped
> enrichments are lost, and JSONL telemetry has correlated
> blank windows.
> Commit prefixes follow `BR-EXTEND-002`.

## Motivation

Three tightly-linked concerns the project has surfaced but
doesn't yet protect against:

1. **OTEL-continuity gap during rebuilds.** Every code change to
   the sidecar requires the same dance: stop blue (`:5050`),
   build (which can finally write to `bin/Debug/`), restart blue.
   Between stop and restart, `/healthz` is unreachable, every
   skill's `!` exec falls through to `PRECONDITION_FAIL`, and
   any in-flight session enrichments are lost. With
   `BR-EXTEND-009`'s plan-tagging now part of the contract,
   continuity is no longer cosmetic — gaps in the JSONL break
   per-plan filtering.

2. **No safe way to validate a build before swapping.** Today,
   the rebuilt binary IS the running binary the moment the
   restart succeeds. If the build is broken (compile error, mis-
   wiring, regression in a code path tests didn't cover), the
   live system breaks. Rolling back means rebuilding from a
   prior commit — more downtime.

3. **No pattern for future tier-managed components.** The
   project's tier philosophy (`BR-PROCESS-008`) names
   `/skill-bootstrap` as the platform-tier owner. When
   kai-platform lands and registers its own platform-tier
   components, they inherit the same gap unless we capture the
   pattern now.

Plan-7 introduces a **stage / promote / discard** lifecycle on
top of the existing `IProcessLifecycle` so a long-running
component can be rebuilt and validated in parallel before the
blue version is touched. Pattern is general; v1 implements it
for the helpers sidecar (the highest-traffic tier-managed
component). Future bootstrap-class components inherit by
adding their own staging spec.

## New / changed business rules

- **`BR-PROCESS-011` — Long-running platform components support
  zero-downtime rebuilds via stage/promote/discard.** Every
  tier-managed component that hosts user-facing skill traffic
  (today: the helpers sidecar) MUST expose three lifecycle
  verbs through its tier-owning skill:

  - `stage` — build to a staging output directory; spawn a
    second instance ("green") on a separate port; verify health.
    Does NOT touch the running blue instance.
  - `promote` — atomic swap. Verify green is healthy → kill
    blue → copy staged binary into the production output dir →
    restart blue → verify → kill green. On any failure, leave
    blue's PID file in a recoverable state (`/skill-bootstrap
    sweep` cleans it).
  - `discard` — kill green; leave blue running unchanged.
    Cleans the green PID file.

  The component's `ComponentSpec` declares its `StagingPort`
  and `StagingPath` alongside the production `Port` and
  `ExePath`. Two PID files (`<name>.pid` for blue,
  `<name>-green.pid` for green) keep state isolation.

  Promote refuses to proceed if green is not healthy; the user
  must `discard` and `stage` again (or pass `--force` to
  override, which lands an `ARCHITECTURE_DECISION_REQUIRED`
  gate per `BR-PROCESS-009` once Plan-6 lands).

  *Why:* the OTEL-continuity gap during rebuilds is now a
  contract violation (`BR-EXTEND-009`'s plan-tagging makes
  telemetry continuity load-bearing). Stage/promote brings the
  gap from the order of seconds (current) down to the order
  of milliseconds (atomic swap, only the moment between
  killing blue and restarting it). It also gives us a real
  rollback path: a broken green doesn't touch blue.

- **`BR-PROCESS-012` — Promote operations are atomic with
  rollback on failure.** During promote:

  1. Verify green is healthy (else refuse).
  2. Snapshot the existing blue binary (in-memory or to a
     `bin/Debug.bak/` directory) so a failure can rewind.
  3. Kill blue → copy staged binary → restart blue → verify
     blue's `/healthz`.
  4. On verify-fail: restore the snapshot, restart blue, leave
     green running so the user can inspect what went wrong.
  5. On verify-pass: kill green, delete green PID file,
     promote complete.

  The state machine MUST never end in "no blue running and no
  green running" except through explicit user `discard +
  stop`. Any internal failure path leaves at least one viable
  instance.

  *Why:* a half-failed promote that leaves the system off is
  worse than the current rebuild gap. Rollback-on-failure is
  the contract that makes promote safer than the current
  stop-build-restart pattern.

## Files affected

### New files

| Path | Purpose |
|---|---|
| `src/HelpersSidecar/Infrastructure/IStageablecomponent.cs` | Optional companion contract — components that support stage/promote register a `StagingSpec` (StagingPort, StagingPath, StagingPidFile). v1 has the sidecar; future kai-platform components plug in. |
| `src/HelpersSidecar/Infrastructure/StagingSpec.cs` | Value object — `StagingPort`, `StagingPath`, `StagingPidFile`, `BuildCommand`, `BuildArgs`. Configurable per component. |
| `tests/HelpersSidecar.Tests/Infrastructure/StageableLifecycleTests.cs` | NEW — covers the state machine: stage when no green, refuses double-stage, refuses promote when green unhealthy, atomic promote (snapshot + restore on fail), discard kills green only. |
| `tests/HelpersSidecar.Tests/Infrastructure/PromoteRollbackTests.cs` | NEW — focused on the BR-PROCESS-012 rollback contract: simulate a verify-fail mid-promote and assert blue is restored from snapshot. |

### Modified files

| Path | Change |
|---|---|
| `src/HelpersSidecar/Infrastructure/ComponentRegistry.cs` | Optional `StagingSpec` on `ComponentSpec`. Default registry adds a sibling "sidecar-green" entry pointing at the staging output. |
| `src/HelpersSidecar/Infrastructure/IProcessLifecycle.cs` | New methods: `Task<StageResult> StageAsync(string componentName, CancellationToken ct = default)`, `Task<PromoteResult> PromoteAsync(string componentName, CancellationToken ct = default)`, `Task<DiscardResult> DiscardAsync(string componentName, CancellationToken ct = default)`. |
| `src/HelpersSidecar/Infrastructure/ProcessLifecycle.cs` | Implementations. Stage builds via the configured build command, spawns green on `StagingPort`, polls `:<StagingPort>/healthz`. Promote runs the BR-PROCESS-012 atomic-swap state machine. Discard kills green + cleans PID file. |
| `src/HelpersSidecar/Application/LifecycleCli.cs` | New CLI verbs: `--lifecycle stage <component>`, `--lifecycle promote <component>`, `--lifecycle discard <component>`. JSON output shape extended. |
| `.claude/skills/skill-bootstrap/SKILL.md` | New verbs documented: `/skill-bootstrap stage`, `/skill-bootstrap promote`, `/skill-bootstrap discard`. Probe table extended with green-side rows when staging is in progress. |
| `.claude/skills/skill-bootstrap/HELP.md` | Verb table updated. |
| `src/HelpersSidecar/Program.cs` | Register the green sibling component in `ComponentRegistry.Default`. Read `StagingPort` + `StagingPath` from `appsettings`. |
| `src/HelpersSidecar/appsettings.json` | New section `Lifecycle:Staging` with `Port`, `OutputPath`, `BuildCommand`. |
| `docs/business-rules.md` | Add `BR-PROCESS-011`, `BR-PROCESS-012`. Amend `BR-PROCESS-008`'s text to point at Plan-7 for staged rebuilds. |
| `CLAUDE.md` | New section "Zero-downtime rebuilds (stage/promote/discard)". Cross-link from the existing tier-philosophy text. |
| `docs/process-incidents.md` | Append entry — "OTEL-continuity gap during rebuilds was a known sharp edge; Plan-7 closes it". |

## Behavioural change

**Before:**

```
$ # change C# code
$ # rebuild requires stopping blue first
$ Stop-Process -Id <pid>
$ dotnet build src/HelpersSidecar              # would-fail-with-DLL-lock workaround
$ ASPNETCORE_ENVIRONMENT=Development dotnet src/HelpersSidecar/.../HelpersSidecar.dll  # restart
$ # OTEL has been off for ~10-30 seconds; session enrichment lost
```

**After:**

```
$ # change C# code
$ /skill-bootstrap stage                       # builds to bin/Staging/, spawns green on :5051
$ # blue keeps running on :5050; OTEL continuous
$ # green's :5051/healthz validates the new build
$ /skill-bootstrap promote                     # atomic swap — sub-second blue restart
$ # OTEL continuity preserved; session-scoped enrichment intact
```

The promote step does have a brief window (Kestrel restart is
not literally zero-downtime — typically 200–500 ms), but the
gap is bounded and predictable. Critically, the *build* phase
no longer requires blue to be down; the long part of the
rebuild cycle stays in green.

## Test approach

Per `BR-PROCESS-007` every test scopes to one domain change.
The lifecycle changes mock at the seam:

- **`StageableLifecycleTests`** — `IPortProbe` + `IComponentRegistry`
  + a mock `IBuildRunner` (new abstraction over `dotnet build` so
  tests don't shell out). State-machine cells:
  - stage when no green: spawns green, returns ok.
  - stage when green already running: returns `AlreadyStaged`.
  - promote when green not healthy: returns `RefusedNotHealthy`.
  - promote when green healthy: snapshot blue → kill blue → copy
    binary → restart blue → verify → kill green. Asserts the
    *order* of operations and the final state.
  - discard: kills green, leaves blue.
- **`PromoteRollbackTests`** — focused on the BR-PROCESS-012
  rollback contract. The mock `IBuildRunner` injects a failure
  at the verify-blue step; assert the snapshot restore brings
  blue back up and green stays alive for inspection.
- **`StagingSpecTests`** — value-object validation: ports must
  differ from production, paths must be writeable, build command
  is non-empty.

End-to-end runtime acceptance happens via the existing
`/skill-bootstrap` flow:

1. Make a trivial code change.
2. `/skill-bootstrap stage` — should build, spawn green, no blue
   restart.
3. `/skill-bootstrap promote` — should swap; `/healthz` on
   `:5050` reports a new uptime; OTEL session enrichment
   preserved (verify by re-reading the persistent map post-
   promote).

Test loop stays domain-scoped. The `IBuildRunner` abstraction
is the cross-domain seam — exercising real `dotnet build` is
opt-in via `[Trait("Scope","cross-domain")]` per `BR-PROCESS-007`.

## Phase ordering

1. **Phase 1 (this commit)** — plan file. `plan:` prefix.

2. **Phase 2a** — Value objects + interfaces. `IStageableLifecycle`
   methods on `IProcessLifecycle`, `StagingSpec` value object,
   `IBuildRunner` abstraction, result records (`StageResult`,
   `PromoteResult`, `DiscardResult`). Tests for the value objects.
   No state-machine implementation yet. `feat(skill-bootstrap):`
   prefix.

3. **Phase 2b** — `ProcessLifecycle` implements StageAsync /
   PromoteAsync / DiscardAsync with the full state machine
   (BR-PROCESS-011 + BR-PROCESS-012). `IBuildRunner` real impl
   wraps `dotnet build`. Tests cover the cells. `feat(skill-bootstrap):`
   prefix.

4. **Phase 2c** — CLI verbs: `--lifecycle stage <name>`,
   `--lifecycle promote <name>`, `--lifecycle discard <name>`.
   JSON output shapes. Tests for CLI argument parsing + output
   shape. `feat(skill-bootstrap):` prefix.

5. **Phase 2d** — `/skill-bootstrap` SKILL.md gains `stage`,
   `promote`, `discard` verbs. Probe table extended to include
   green when staging is active. HELP.md updated. `refactor(skill-bootstrap):`
   prefix.

6. **Phase 2e** — appsettings.json + ComponentRegistry registration
   for the green sibling. `feat(skill-bootstrap):` prefix.

7. **Phase 2f** — Docs: BR-PROCESS-011, BR-PROCESS-012, BR-PROCESS-008
   text amendment. CLAUDE.md "Zero-downtime rebuilds" section.
   process-incidents entry. `docs:` prefix.

8. **Phase 3 — Build.** `dotnet build`. `chore:` prefix only if
   artefacts changed.

9. **Phase 4 — Test.** Full suite + manual stage→promote dogfood
   run. `test:` prefix.

10. **Phase 5 (acceptance)** — make a trivial code change (a
    no-op log message), run stage→promote, confirm OTEL session
    continuity by inspecting the JSONL for an unbroken `plan` tag
    spanning the promote moment.

## Rollback

Each phase commits separately:

1. `git revert <plan-commit>` — drops Plan-7.
2. `git revert <2a..2f>` (in reverse order) — removes the
   stage/promote machinery. Existing components fall back to
   stop-build-restart.
3. `git revert <3-chore>` / `<4-test>` as needed.

Reverting Phase 2b specifically removes the state machine but
leaves the value objects + DI registration; useful if the state
machine has a bug that's not yet diagnosed but the user wants
the API surface preserved for a later rewrite.

## Out of scope

- **Stage/promote for the Go collector.** The collector is built
  via OCB (Go), not `dotnet build`. The same pattern applies but
  the build runner abstraction is different. v1 is sidecar-only;
  collector staging is a follow-up plan when the .NET-only
  collector pivot lands (originally floated in Plan-4's "Out of
  scope — deferred to Plan-5" notes; remains deferred).

- **Concurrent multiple greens.** v1 supports one staged version
  at a time per component. A second `/skill-bootstrap stage`
  while a green exists returns `AlreadyStaged`; the user
  `discard`s before re-staging. Multi-green is unnecessary for
  the rebuild use-case.

- **Network-level traffic shifting.** Promote is a process-level
  swap; it does NOT do gradual traffic shifts (e.g. 10% green /
  90% blue). All-or-nothing. For sidecar workloads (skill
  dispatches) this is correct; gradual shifts are a feature of
  load-balanced deployments, not a single-host platform.

- **Auto-promote on green-healthy.** The user explicitly types
  `promote`; no auto-promote even when green has been healthy
  for N seconds. This preserves the "human in the loop" property
  of the lifecycle commands.

- **Backout on user-initiated rollback.** If the user wants to
  undo a successful promote, they re-stage from a prior commit.
  v1 does NOT keep "last-known-good blue" beyond the
  promote-time snapshot. A "rollback" verb is a v2 candidate.

## What kai-platform inherits for free

When `KaiPlatformDomain` lands and registers its own
platform-tier components (e.g. a `KaiPlatformSidecar` if the
domain has its own deterministic-helpers service), each
component implements the same `StagingSpec` shape and gains
stage/promote/discard automatically through `IProcessLifecycle`.
Zero new code per component beyond the spec.

This is the test of "did we draw the abstraction at the right
boundary?" — the abstraction is `IProcessLifecycle`, and adding
a new component to it doesn't ripple into any existing skill.

## What this plan deliberately does NOT change

The existing `/skill-bootstrap start` / `/skill-bootstrap stop`
verbs are unchanged. They continue to be the canonical
bring-up / tear-down for a clean machine. Stage/promote/discard
are *additive* — they're how you rebuild without downtime once
the platform is already running. A first-time user types
`/skill-bootstrap install` then `/skill-bootstrap start` and
never touches stage/promote until they're modifying code.
