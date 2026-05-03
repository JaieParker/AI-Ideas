# OTLP port single source of truth + self-bring-up chaining for skills

> Plan-13 closes two gaps surfaced by a routine `/demo otel` run on
> 2026-05-04. The collector's bind port and the sidecar's
> port-probe drifted (collector hardcoded to `:4318` in
> `config.yaml`; sidecar's typed option `Otel:CollectorOtlpPort`
> read 4318 by default but a local user with `:4318` already held
> by `ClaudeObserver.Api` had no clean way to re-port both sides
> from one place). And `/demo`'s pre-flight told the user "fix:
> /otel up" without ever offering to chain it — the same
> name-but-don't-offer pattern repeats across other skills.

## Motivation

Two distinct but related problems, both observed in one session:

1. **Port drift.** `config.yaml` hardcodes `127.0.0.1:4318`. The
   .NET sidecar reads `Otel:CollectorOtlpPort` from typed
   options (default 4318; `appsettings.Development.json`
   overrides to 14318). When a user has `:4318` taken locally and
   re-ports, they must edit `config.yaml`, set
   `ASPNETCORE_ENVIRONMENT=Development`, AND ensure
   `appsettings.Development.json` matches — three edits, two
   sources of truth, plus an environment toggle. Today's incident:
   `ClaudeObserver.Api` (PID 13400) holds `:4318`, the user
   re-ported `config.yaml` to `:14318`, and `/demo`'s pre-flight
   still failed STEP 00.e because it probed `:4318` (running in
   Production env, no `appsettings.Development.json` override
   loaded). Three places to keep aligned, easy to drift, and
   `BR-CODE-001` violated by `OtelDomain.cs:54` where the port
   is even baked into a description string.

2. **Skills name recovery actions but never offer to invoke
   them.** `/demo` pre-flight on collector-down prints "fix:
   /otel up" and stops. `/otel` skill on sidecar-down prints
   "Run /skill-bootstrap status, then /skill-bootstrap start"
   and stops. The user has to read the line, copy the next
   command, type it, and re-run the original. The skills are
   supposed to **bring themselves online** — name + offer +
   chain via the Skill tool, gated on user confirmation. Today
   they only name.

Plan-13 fixes both. One source of truth for the OTLP port —
the .NET sidecar's typed option — propagated to the Go collector
via OTel-native `${env:NAME:-default}` substitution. And a
structured `RECOVERY_AVAILABLE` marker emitted by skills whose
pre-flight detects a recoverable down-state, paired with a new
skill-body convention that tells Claude how to interpret the
marker (offer, confirm, chain).

## Files affected

| Path | Change |
|---|---|
| `config.yaml` | Replace hardcoded `127.0.0.1:4318` with `127.0.0.1:${env:OTEL_COLLECTOR_OTLP_HTTP_PORT:-4318}` (OTel collector env-var substitution). |
| `config.acceptance.yaml` | **Delete.** No longer needed — single `config.yaml` covers both default and re-port via env var. |
| `src/HelpersSidecar/Program.cs` | Add gitignored `appsettings.Local.json` to the configuration builder (loaded after `appsettings.{Env}.json`); clarify with comment that `Otel:CollectorOtlpPort` is the single source. |
| `src/HelpersSidecar/appsettings.Development.json` | Drop `CollectorConfigFile` and `CollectorOtlpPort` overrides (no longer needed; user sets in `appsettings.Local.json` or via env var instead). |
| `src/HelpersSidecar/appsettings.Local.json.example` | New template the user copies to `appsettings.Local.json` to set `Otel:CollectorOtlpPort` locally. Includes one short comment block. |
| `src/HelpersSidecar/Infrastructure/ProcessLifecycle.cs` | When spawning the collector, set `OTEL_COLLECTOR_OTLP_HTTP_PORT` env var on the child `ProcessStartInfo` from `Otel:CollectorOtlpPort` (resolved via `IConfiguration` injected through the registry; see Phase 2 detail). |
| `src/HelpersSidecar/Endpoints/DemoDispatchEndpoint.cs` | When pre-flight 00.b (collector control) FAILs AND 00.e (port) PASSes, emit `RECOVERY_AVAILABLE: skill="otel" verb="up" reason="collector control down; port :NNNN free"` immediately after the pre-flight block. Also drop the magic-number fallback `DefaultOtlpHttpPort = 4318` const at file scope (kept only as a one-shot literal in the `GetValue` call). |
| `src/HelpersSidecar/Domain/OtelDomain.cs:54` | Remove the `:4318`/`:13133`/`:13134` literals from the description; replace with port-agnostic phrasing ("Receives OTLP, exposes control + healthz APIs"). |
| `.claude/skills/demo/SKILL.md` | Add a body section that tells Claude: if the dispatch output contains `RECOVERY_AVAILABLE:`, parse the `skill` and `verb`, ask the user "invoke `/<skill> <verb>` to bring it up?", and on confirmation invoke the named skill via the `Skill` tool. After the recovery skill returns, re-invoke `/demo`. |
| `.claude/skills/otel/SKILL.md` | Same convention — if the dispatch output contains `RECOVERY_AVAILABLE: skill="skill-bootstrap"`, offer to chain. Today this case is already handled at the SKILL.md `!` exec line by `PRECONDITION_FAIL`; keep that, but add the chaining offer below it. |
| `.claude/skills/skill-bootstrap/SKILL.md` | Audit: pre-flight already names recovery commands as instructions to the user. Add the `RECOVERY_AVAILABLE:` marker emit when bootstrap's lifecycle probe returns `Zombie` (offer to sweep) or `NotRunning` (offer to start), so the chain works the other direction too. (Note: `/skill-bootstrap` is the bootstrap-class exception per `BR-PROCESS-001`; it doesn't need the sidecar, but it can still offer the chain itself.) |
| `docs/business-rules.md` | Add `BR-OTEL-007` (single-source OTLP port) and `BR-SKILL-014` (pre-flight emits structured `RECOVERY_AVAILABLE` marker). |
| `.gitignore` | Add `src/HelpersSidecar/appsettings.Local.json`. (No need to ignore `config.acceptance.yaml` after its deletion — drop that line too.) |
| `tests/HelpersSidecar.IntegrationTests/Demo/DemoPortProbeFollowsConfigTests.cs` | New — `BR-OTEL-007` tests: when `Otel:CollectorOtlpPort=14318` is bound in test config, DemoDispatch's pre-flight probes `:14318` and the output references `:14318` consistently. |
| `tests/HelpersSidecar.IntegrationTests/Demo/DemoEmitsRecoveryAvailableMarkerTests.cs` | New — `BR-SKILL-014` tests: when collector down + port free, output contains exactly one `RECOVERY_AVAILABLE: skill="otel" verb="up"` line; when port held by other process, no `RECOVERY_AVAILABLE` (port conflict is not auto-recoverable per `BR-SECURITY-003`). |
| `tests/HelpersSidecar.IntegrationTests/Lifecycle/CollectorSpawnPropagatesPortEnvTests.cs` | New — `BR-OTEL-007` tests: `ProcessLifecycle.SpawnAsync("collector")` sets `OTEL_COLLECTOR_OTLP_HTTP_PORT` on the child process env from `Otel:CollectorOtlpPort` (via a fake `IProcessStarter` that captures the `ProcessStartInfo`). |
| `.claude/skills/architecture-review/SKILL.md` | Change `disable-model-invocation: true` → `false` so `/extend-skills` Phase 1.5 can chain via the `Skill` tool. The HITL gate per `BR-PROCESS-009` is preserved — each `ARCHITECTURE_DECISION_REQUIRED` still requires the user to record a resolution before Phase 2 proceeds. The model invokes; the user decides. |
| `.claude/skills/extend-skills/SKILL.md` | Phase 1.5 instructions: after the plan-file commit, **invoke `/architecture-review <plan-file>` via the `Skill` tool** (no longer "ask the user to type it"). Then loop on each `ARCHITECTURE_DECISION_REQUIRED` block, asking the user for a resolution word per block. |
| `docs/business-rules.md` (`BR-PROCESS-009`) | Amend to make explicit that Phase 1.5's *invocation* of `/architecture-review` may be chained by `/extend-skills`; the *decision recording* remains user-only. Resolution kind: **Evolve** (candidate — recorded in this plan's review section after Phase 1.5 runs). |

## Behavioural change

**Before:**

- OTLP port lives in three places that must be hand-aligned:
  `config.yaml` (hardcoded `4318`), `appsettings.json`
  (`Otel:CollectorOtlpPort`, default 4318), and an optional
  `appsettings.Development.json` + `config.acceptance.yaml`
  pair (gated on `ASPNETCORE_ENVIRONMENT=Development`). Local
  re-port requires editing two files plus an env-var toggle.
- `OtelDomain.cs:54` description string contains literal `:4318`,
  drifting silently if the port is changed.
- `/demo` pre-flight on collector-down prints "fix: /otel up"
  and stops. User reads, copies, types `/otel up`, re-runs `/demo`.
- `/otel` skill on sidecar-down prints "Run /skill-bootstrap
  status, then /skill-bootstrap start" and stops. Same dance.

**After:**

- OTLP port has **one source of truth**: `Otel:CollectorOtlpPort`
  in the .NET sidecar's typed options. Default 4318 in tracked
  `appsettings.json`. Local user overrides in
  gitignored `appsettings.Local.json`. The sidecar exports
  `OTEL_COLLECTOR_OTLP_HTTP_PORT` to the spawned Go collector,
  which `config.yaml` consumes via OTel's native env-var
  substitution. Editing one file moves both halves.
- `/demo` pre-flight, when collector down + port free, emits
  `RECOVERY_AVAILABLE: skill="otel" verb="up" reason="..."`. The
  `demo` SKILL.md instructs Claude: parse this marker, ask the
  user "invoke /otel up to bring the collector up?", on
  confirmation call `Skill(otel, "up")` then re-invoke `/demo`.
- Same pattern at `/otel` SKILL.md → `RECOVERY_AVAILABLE:
  skill="skill-bootstrap" verb="start"` when the sidecar is the
  obstacle. (The chaining offer sits below the existing
  PRECONDITION_FAIL handling — they don't conflict; PRECONDITION
  fires when the sidecar is *fully unreachable* and the dispatch
  never runs.)
- `/extend-skills` Phase 1.5 chains `/architecture-review` via
  the `Skill` tool automatically — today the skill is blocked by
  `disable-model-invocation: true` despite the playbook
  expecting the chain. Flipping the flag closes the gap. The
  human gate stays at the *decision recording* step (each
  `ARCHITECTURE_DECISION_REQUIRED` resolution requires the user
  to type a word) — the gate moves from "user types the
  invocation" to "user types the decision", which is the gate
  that actually matters for `BR-PROCESS-009`.

## Test approach

Adds three new integration test files; covers two new BRs.

- `BR-OTEL-007 — OTLP port has a single source of truth (the
  sidecar's typed options); the collector's bind port is
  propagated via env-var substitution; no other component
  hardcodes the port.`
  - `DemoPortProbeFollowsConfigTests.cs` — drive
    `Otel:CollectorOtlpPort=14318`, run dispatch, assert output
    contains `:14318` (port probe) and not `:4318` outside the
    one default-fallback literal.
  - `CollectorSpawnPropagatesPortEnvTests.cs` — fake the
    process starter, drive `Otel:CollectorOtlpPort=15318`, call
    `SpawnAsync("collector")`, assert the captured PSI has
    `OTEL_COLLECTOR_OTLP_HTTP_PORT=15318`.
  - Manual: `grep -rn "4318" src/ config.yaml | wc -l` should be
    one (the default fallback in
    `appsettings.json` only).

- `BR-SKILL-014 — pre-flight checks must emit a structured
  RECOVERY_AVAILABLE marker when a named-recovery action exists;
  skill bodies interpret it to offer (not auto-invoke) the chain.`
  - `DemoEmitsRecoveryAvailableMarkerTests.cs` — three cases:
    (a) collector down + port free → marker emitted with
    `skill="otel" verb="up"`; (b) collector down + port held by
    other process → NO marker (per BR-SECURITY-003 we never
    auto-recommend stopping a process we don't own); (c) all
    pre-flight green → no marker.

- Existing tests stay green: `BR-DEMO-*`, `BR-OTEL-005`
  (port-conflict messaging), `BR-EXTEND-010`, etc.

- `BR-PROCESS-009 amendment — /extend-skills Phase 1.5 may chain
  /architecture-review automatically; the user gate remains at
  decision recording, not invocation.`
  - No new dedicated test file needed; covered by manual
    Phase 1.5 verification on this very plan run (the chain
    either works or it doesn't). Once landed, future
    `/extend-skills` runs serve as ongoing evidence.
  - Audit: `grep -rn "disable-model-invocation: true"
    .claude/skills/` should not include any skill the
    `/extend-skills` playbook expects to chain. Today
    `architecture-review` is the only offender; this plan
    fixes it.

## Architecture review decisions

> BR-PROCESS-009 gate. After Phase 1's plan-file commit,
> `/architecture-review docs/otel/plans/The-OTEL-Plan-13-otlp-port-single-source-and-self-bringup.md`
> produces the structured review. Decisions recorded here.

(Filled after Phase 1.5.)

## Rollback steps

If the change has to be reverted after landing, the rollback is:

1. `git revert <feat-commit-sha>` (filled in after Phase 2 commits)
2. `git revert <chore-commit-sha>` (filled in after Phase 3 if binaries were rebuilt)
3. `git revert <test-commit-sha>` (filled in after Phase 4)

Local users with an `appsettings.Local.json` keep their file (it
was never tracked); they just regain the old two-file pattern via
the reverted code.

## Out of scope

- Changing `:13133` (collector control) or `:13134` (collector
  healthz) ports — they live in the same `config.yaml`, but
  nobody has hit a conflict on them. Same env-var-substitution
  treatment can land in a future plan if needed.
- Changing `:5050` (sidecar listener). It's already a typed
  option; no Go-side propagation needed.
- Generalising `RECOVERY_AVAILABLE` to non-skill recovery (e.g.
  "open this URL"). Out of scope; the marker is skill→skill only.
- Auto-invoking the recovery skill without user confirmation.
  `BR-SECURITY-003`'s spirit (no destructive/state-changing
  action without explicit consent) applies here too — the marker
  triggers an *offer*, never a silent chain.
