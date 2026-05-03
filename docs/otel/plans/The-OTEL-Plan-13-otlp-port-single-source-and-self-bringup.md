# Collector ports single source of truth + self-bring-up chaining for skills

> **Phase-2 scope expansion (2026-05-04):** the original plan
> covered only `:4318` (OTLP receiver). Mid-Phase 2, the user
> flagged that `:13133` (collector control), `:13134`
> (collector healthz), and `:4319` (downstream `otlphttp`
> exporter) are the same drift class — hardcoded in `config.yaml`
> and in several .NET sites. Same defect, same mechanism; doing
> OTLP only would set up the next surprise. Scope expanded to
> cover all four ports under one typed `CollectorOptions`
> class, propagated as env vars, consumed by `config.yaml` via
> the same OTel-native substitution. The architectural EXTENDS
> resolutions from Phase 1.5 are unchanged — this is the same
> pattern applied to more ports.

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
| `config.yaml` | Replace **all four** hardcoded ports with OTel-native env-var substitution: `:4318` → `${env:CLAUDE_OTEL_OTLP_HTTP_PORT:-4318}` (OTLP receiver), `:13133` → `${env:CLAUDE_OTEL_CONTROL_PORT:-13133}` (enrichmentctl extension), `:13134` → `${env:CLAUDE_OTEL_HEALTHZ_PORT:-13134}` (health_check extension), `:4319` → `${env:CLAUDE_OTEL_DOWNSTREAM_OTLP_PORT:-4319}` (otlphttp exporter). |
| `src/HelpersSidecar/Infrastructure/CollectorOptions.cs` | **New file.** Typed-options class binding `Otel` section: `CollectorExePath`, `CollectorConfigFile`, `CollectorOtlpPort` (4318), `CollectorControlPort` (13133), `CollectorHealthzPort` (13134), `DownstreamOtlpPort` (4319). Section name + canonical defaults centralised here. |
| `config.acceptance.yaml` | **Delete.** No longer needed — single `config.yaml` covers both default and re-port via env var. |
| `src/HelpersSidecar/Program.cs` | Add gitignored `appsettings.Local.json` to the configuration builder (loaded after `appsettings.{Env}.json`). Register `CollectorOptions` via `builder.Services.Configure<CollectorOptions>(builder.Configuration.GetSection("Otel"))`. Pass all four ports to `ComponentRegistry.Default`. |
| `src/HelpersSidecar/appsettings.json` | Add three new keys under `Otel`: `CollectorControlPort` (13133), `CollectorHealthzPort` (13134), `DownstreamOtlpPort` (4319), alongside existing `CollectorOtlpPort`. |
| `src/HelpersSidecar/Infrastructure/CollectorControlClient.cs` | Replace hardcoded `:13133` and `:13134` constants with values resolved from `IOptions<CollectorOptions>`. URLs (`ControlBase`, `HealthBase`) become per-instance properties. |
| `src/HelpersSidecar/Endpoints/OtelDispatchEndpoint.cs` | Replace literal `:13133` in `CollectorDownMessage` with the resolved port; same for any other inline literal. Inject `IOptions<CollectorOptions>` if needed. |
| `src/HelpersSidecar/Endpoints/EnrichDispatchEndpoint.cs` | Same — replace literal `:13133` in error message with the resolved port. |
| `src/HelpersSidecar/appsettings.Development.json` | Drop `CollectorConfigFile` and `CollectorOtlpPort` overrides (no longer needed; user sets in `appsettings.Local.json` or via env var instead). |
| `src/HelpersSidecar/appsettings.Local.json.example` | New template the user copies to `appsettings.Local.json` to set `Otel:CollectorOtlpPort` locally. Includes one short comment block. |
| `src/HelpersSidecar/Infrastructure/ProcessLifecycle.cs` | Apply per-spec `EnvironmentVariables` (new dict on `ComponentSpec`) to the spawned child's `ProcessStartInfo.Environment`. The collector spec carries all four `CLAUDE_OTEL_*_PORT` vars from `CollectorOptions`; future tier-managed components can declare their own env vars. |
| `src/HelpersSidecar/Infrastructure/ComponentRegistry.cs` | Extend `ComponentSpec` with `IReadOnlyDictionary<string, string>? EnvironmentVariables`. Populate the collector spec with all four `CLAUDE_OTEL_*_PORT` vars derived from `CollectorOptions`. Centralise the canonical env-var names as `public const string CollectorOtlpPortEnvVar = "CLAUDE_OTEL_OTLP_HTTP_PORT"` etc. |
| `src/HelpersSidecar/Endpoints/DemoDispatchEndpoint.cs` | When pre-flight 00.b (collector control) FAILs AND 00.e (port) PASSes, emit `RECOVERY_AVAILABLE: skill="otel" verb="up" reason="collector control down; port :NNNN free"` immediately after the pre-flight block. Also drop the magic-number fallback `DefaultOtlpHttpPort = 4318` const at file scope (kept only as a one-shot literal in the `GetValue` call). |
| `src/HelpersSidecar/Domain/OtelDomain.cs:54` | Remove the `:4318`/`:13133`/`:13134` literals from the description; replace with port-agnostic phrasing ("Receives OTLP, exposes control + healthz APIs"). |
| `.claude/skills/demo/SKILL.md` | Add a body section that tells Claude: if the dispatch output contains `RECOVERY_AVAILABLE v1`, parse the `skill` and `verb`, ask the user "invoke `/<skill> <verb>` to bring it up?", and on confirmation invoke the named skill via the `Skill` tool. After the recovery skill returns, re-invoke `/demo`. **Frontmatter `allowed-tools` adds:** `Skill(otel up *)`. |
| `.claude/skills/otel/SKILL.md` | Same convention — if the dispatch output contains `RECOVERY_AVAILABLE v1: skill="skill-bootstrap"`, offer to chain. Today this case is already handled at the SKILL.md `!` exec line by `PRECONDITION_FAIL`; keep that, but add the chaining offer below it. **Frontmatter `allowed-tools` adds:** `Skill(skill-bootstrap start *)`. |
| `.claude/skills/skill-bootstrap/SKILL.md` | Audit: pre-flight already names recovery commands as instructions to the user. Add the `RECOVERY_AVAILABLE v1:` marker emit when bootstrap's lifecycle probe returns `Zombie` (offer to sweep) or `NotRunning` (offer to start), so the chain works the other direction too. (Note: `/skill-bootstrap` is the bootstrap-class exception per `BR-PROCESS-001`; it doesn't need the sidecar, but it can still offer the chain itself.) **Frontmatter `allowed-tools`:** no Skill chain (this skill is leaf) — sweep/start are dispatched via its existing `Bash(dotnet *)` lifecycle CLI. |
| `docs/business-rules.md` | (1) Add `BR-OTEL-007` (single-source OTLP port). (2) Add `BR-SKILL-014` (pre-flight emits structured `RECOVERY_AVAILABLE v1` marker; offer-then-chain on user confirm; never auto-invoke). (3) **Amend `BR-SKILL-002`** — define "side effects" as state-changing (file writes outside `output/<owner>/`, network beyond local sidecars, mutations to persistent state, process spawn/kill); explicitly exempt read-only review/report skills whose only output is a report (in-memory or `output/`-only). (4) **Amend `BR-PROCESS-009`** — distinguish *invocation* (may chain via Skill tool) from *decision recording* (HITL-only); name report generation as HITL input, not the gate. |
| `src/HelpersSidecar/Artefacts/ArtefactSpecs.cs` | Add `appsettings.Local.json` as an `ArtefactSpec` (Name: `"sidecar-local-settings"`, Lifecycle: `UserEdited`, GitTracked: false, Owner: `"cross-domain"`, GoverningBR: `"BR-OTEL-007"`, Producer: human-edited note). `IArtefactWriter.Write` refuses programmatic writes per `BR-PROCESS-015`. |
| `.gitignore` | Add `src/HelpersSidecar/appsettings.Local.json`. (No need to ignore `config.acceptance.yaml` after its deletion — drop that line too.) |
| `tests/HelpersSidecar.IntegrationTests/Demo/DemoPortProbeFollowsConfigTests.cs` | New — `BR-OTEL-007` tests: when `Otel:CollectorOtlpPort=14318` is bound in test config, DemoDispatch's pre-flight probes `:14318` and the output references `:14318` consistently. |
| `tests/HelpersSidecar.IntegrationTests/Demo/DemoEmitsRecoveryAvailableMarkerTests.cs` | New — `BR-SKILL-014` tests: when collector down + port free, output contains exactly one `RECOVERY_AVAILABLE: skill="otel" verb="up"` line; when port held by other process, no `RECOVERY_AVAILABLE` (port conflict is not auto-recoverable per `BR-SECURITY-003`). |
| `tests/HelpersSidecar.IntegrationTests/Lifecycle/CollectorSpawnPropagatesPortEnvTests.cs` | New — `BR-OTEL-007` tests: `ProcessLifecycle.SpawnAsync("collector")` sets `CLAUDE_OTEL_OTLP_HTTP_PORT` on the child process env from `Otel:CollectorOtlpPort` (via a fake `IProcessStarter` that captures the `ProcessStartInfo`). |
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
  `CLAUDE_OTEL_OTLP_HTTP_PORT` to the spawned Go collector,
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
    `CLAUDE_OTEL_OTLP_HTTP_PORT=15318`.
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
  decision recording, not invocation. The report itself is HITL
  input.`
  - No new dedicated test file needed; covered by manual
    Phase 1.5 verification on this very plan run (the chain
    either works or it doesn't). Once landed, future
    `/extend-skills` runs serve as ongoing evidence.
  - Audit: `grep -rn "disable-model-invocation: true"
    .claude/skills/` should not include any skill the
    `/extend-skills` playbook expects to chain. Today
    `architecture-review` is the only offender; this plan
    fixes it.

- `BR-SKILL-002 amendment — define "side effects" as state-
  changing; exempt read-only review/report skills.`
  - Existing tests for `disable-model-invocation` presence on
    side-effecting skills remain green.
  - New negative case: `/architecture-review` test verifies the
    skill produces no file write outside `output/architecture-
    reviews/` and no mutation to persistent state, justifying
    the exemption.

- `BR-SKILL-009 enforcement — every new Skill(...) chain is
  matched by a tightest-prefix allowed-tools entry.`
  - Manual: `grep -rn "allowed-tools:" .claude/skills/` after
    Phase 2 — every SKILL.md that adds a chained `Skill(...)`
    call has a matching tightest-prefix entry; no bare `Skill`.
  - **Live verification (BR-SKILL-009 / BR-SKILL-013 hybrid):**
    Phase 4 must run `/demo otel` end-to-end *with the collector
    pre-stopped* and confirm:
    1. Pre-flight produces a `RECOVERY_AVAILABLE v1` marker.
    2. The skill body's offer-then-chain loop fires (user
       confirms, `Skill(otel "up")` is invoked).
    3. After `/otel up` returns, `/demo otel` re-invokes and
       completes the 14 live steps.
    If any step fails, Phase 4's "keep / revert" decision must
    weigh whether the mechanism actually works on a clean run.

- `BR-SKILL-013 — every skill remains 8/8 on the 4 D rubric.`
  - Phase 4 runs `/ai-level local`, asserts every skill scores
    8/8 (no regression from Plan-12's full-marks state), with
    particular attention to the five touched skills (`demo`,
    `otel`, `skill-bootstrap`, `architecture-review`,
    `extend-skills`).

- `BR-PROCESS-013 — the new RECOVERY_AVAILABLE v1 schema marker
  appears in every consumer's body and producer's output.`
  - Producer assertion: `DemoEmitsRecoveryAvailableMarkerTests`
    asserts the literal `RECOVERY_AVAILABLE v1:` prefix.
  - Consumer assertion: `grep -n "RECOVERY_AVAILABLE v1"
    .claude/skills/{demo,otel,skill-bootstrap}/SKILL.md` matches
    in every consumer SKILL.md body.

- `BR-PROCESS-015 — appsettings.Local.json is registered.`
  - `ArtefactSpecsTests.Sidecar_Local_Settings_Is_UserEdited`
    asserts the spec exists with `Lifecycle = UserEdited`,
    `GitTracked = false`, `IArtefactWriter.Write` raises
    `InvalidOperationException`.

## Architecture review decisions

> BR-PROCESS-009 gate. `/architecture-review` ran 2026-05-04;
> response recorded eight `ARCHITECTURE_DECISION_REQUIRED`
> blocks plus five `OUT-OF-SCOPE` `QC:` notes. Resolutions:

- **BR-SKILL-002** (User-only skills set `disable-model-invocation: true`): **Resolution: Evolve** — amend the rule to define "side effects" precisely. The narrow definition is *state-changing effects* (file writes outside `output/<owner>/`, network calls beyond local sidecars, mutations to persistent enrichments, process spawn/kill). Read-only judgement skills whose only output is a *report* (in-memory or written to `output/`) are explicitly exempt — the report contents *are* the output, equally valid in memory if the file write is denied. `/architecture-review` is the canonical exempt skill.
- **BR-SKILL-009** (allowed-tools tightest prefix): **Resolution: Constrain** — Plan-13 must enumerate every new `Skill(...)` allowed-tools entry per skill before Phase 2 begins (added to "Files affected" below). Phase 4 must verify `/demo otel` still runs end-to-end and that the chained recovery path (collector down → marker → user confirms → `Skill(otel "up")` invocation) actually works.
- **BR-SKILL-013** (4 D rubric): **Resolution: Constrain** — Phase 4 re-runs `/ai-level local` and asserts every touched skill (plus those that weren't touched) remains 8/8.
- **BR-PROCESS-009** (Architecture evolution requires explicit human decision): **Resolution: Evolve** — amend the rule to make explicit that the *report generation* is HITL input (not the gate); Claude *invokes* the review skill so the report comes back; the *decision recording* is the HITL gate. Part of the report's job is exactly to identify which decisions Claude can make vs which require human judgement, so generating it without Claude triggering it would be a contradiction.
- **BR-PROCESS-013** (Multi-step lifecycle reports schema-versioned): **Resolution: Constrain** — version the marker as `RECOVERY_AVAILABLE v1` in the producing dispatch endpoints and consumer SKILL.md bodies. Do NOT register in `IArtefactRegistry` (the marker is inline output, not a durable file).
- **BR-PROCESS-015** (Every durable artefact registered): **Resolution: Constrain** — register `appsettings.Local.json` as a `UserEdited` `ArtefactSpec` in Phase 2 (Owner: `cross-domain` since the sidecar is platform infra; Lifecycle: `UserEdited`; GitTracked: false; GoverningBR: `BR-OTEL-007`).
- **BR-PROCESS-005** (Flag architectural decisions; enumerate alternatives): **Resolution: Constrain** — added "Alternatives considered" section below enumerating four upstream-supported mechanisms with trade-offs.
- **BR-PROCESS-006** (≥ 3 orthogonal perspectives): **Resolution: Constrain** — added "Perspectives" section below covering engineering / operations / security / strategy.

QC notes folded into the plan body:

- **Env-var name avoiding `OTEL_*` SDK namespace.** Renamed from `CLAUDE_OTEL_OTLP_HTTP_PORT` to `CLAUDE_OTEL_OTLP_HTTP_PORT` throughout the plan. The OpenTelemetry SDK reserves `OTEL_*` for spec-defined variables (per https://opentelemetry.io/docs/specs/otel/configuration/sdk-environment-variables/); a future SDK addition could collide.
- **Env-var name as a typed option** (`Otel:CollectorOtlpPortEnvVarName` defaulting to `CLAUDE_OTEL_OTLP_HTTP_PORT`): **Defer** — not strictly necessary; a single test asserting the literal-string agreement between the `ProcessLifecycle` spawn site and `config.yaml`'s substitution is cheaper.
- **Recursive Phase-0 self-bring-up** for `/extend-skills` itself (offer to `/otel up` when `/enrich plan` would fail): **Defer** — listed as a follow-up in "Out of scope" since BR-EXTEND-009 doesn't yet require it.
- **Staging port unaffected by env-var.** Plan adds an explicit note in `ProcessLifecycle.cs` Phase 2 implementation: staging always uses `Lifecycle:Staging:SidecarPort`, never the env var path.
- **`config.acceptance.yaml` reference audit.** Phase 2 must `grep -rn "config.acceptance.yaml"` and clean any stale references (test fixtures, docs, README pointers) before deleting the file.

## Alternatives considered

The cross-runtime port-alignment problem has at least four
upstream-supported mechanisms. Plan-13 picks **(1) env-var
substitution + sidecar-exports-the-var**. The trade-offs:

| # | Mechanism | What it does | Trade-offs |
|---|---|---|---|
| 1 | **Env-var substitution in `config.yaml`** (`${env:NAME:-default}`) | Sidecar sets the env var on the spawned collector process; `config.yaml` substitutes at collector startup. Stable since v0.65; default-value syntax stable around v0.97 (`confmap` v1 RFC). [docs](https://opentelemetry.io/docs/collector/configuration/#environment-variables) | **Pros:** zero-touch for the collector binary; the substitution is a documented OTel feature; one assignment in the sidecar's spawn site and one substitution in YAML; the value never appears in any tracked file. **Cons:** env-var name is itself a magic string in two places (we cover via test); recursive substitution is a known footgun (avoid by using explicit `env:` scheme). |
| 2 | **`--set` command-line override** (`--set receivers::otlp::protocols::http::endpoint=...`) | Sidecar passes `--set` argv to the spawned collector; merged after all `--config` sources. [docs](https://opentelemetry.io/docs/collector/configuration/#override-settings) | **Pros:** documented; same precedence model as `--config`; final overlay so it always wins. **Cons:** `::` syntax for nested keys is OTel-specific and verbose; cannot set keys containing `.` or `=`; couples the sidecar to the collector's exact YAML key path (refactoring `receivers.otlp...` breaks the override silently). |
| 3 | **Multiple `--config` sources merged** (`--config file:config.yaml --config yaml:'receivers...'`) | Pass an inline YAML overlay via the `yaml:` provider scheme. [docs](https://opentelemetry.io/docs/collector/configuration/#location) | **Pros:** structured override; later sources win on deep-merge. **Cons:** YAML-as-string in argv is awkward to escape; lists are replaced wholesale (not deep-merged), so a future override of e.g. an exporters list is brittle. |
| 4 | **Sidecar generates `config.yaml` from a template** | Sidecar reads a template (e.g. `config.template.yaml`), substitutes its own typed options, writes a per-spawn `config.yaml`. | **Pros:** total control; no env-var contract; easy to test in isolation. **Cons:** custom code we maintain; another file in the spawn dir; the rendered file divergence from the source is a foot-gun for git-status; loses OTel's documented mechanism in favour of our own. |

**Why (1) wins:** smallest change to the project's footprint
(one new env-var assignment, one new substitution); the
mechanism is documented at OpenTelemetry's spec level, so a
future contributor recognises the pattern; the YAML stays
human-readable (not generated). Trade-off accepted: env-var
name is duplicated across .NET spawn site and YAML — covered by
an integration test that fails fast if either side changes
without the other.

**Why not (2):** `--set` couples the sidecar to the collector's
nested YAML key path; refactoring receivers' shape breaks the
override silently.

**Why not (3):** YAML-as-argv escaping plus list-replacement
semantics are risky for any future override beyond a single
scalar.

**Why not (4):** trades a documented mechanism for project-
specific code we'd own and test ourselves.

## Perspectives

Per `BR-PROCESS-006`, ≥ 3 orthogonal lenses on the chosen
approach (env-var substitution + sidecar-exports):

- **Engineering** — adds two lines (one env-var assignment in
  `ProcessLifecycle`, one substitution in `config.yaml`); removes
  three drift sites (acceptance config file, env-gated
  appsettings overlay, hardcoded literal in `OtelDomain.cs`).
  Adds three integration tests (port-probe, spawn-env, marker)
  with clean seam mocks (`IPortProbe`, `IProcessStarter`,
  `ICollectorControlClient`). Net: -2 files, +3 tests, -1 magic
  number.

- **Operations** — fewer surfaces to inspect when debugging "why
  is the collector binding the wrong port?": one place to look
  (`appsettings.Local.json` → resolved `Otel:CollectorOtlpPort`
  → `CLAUDE_OTEL_OTLP_HTTP_PORT` on the spawned process). Caveat:
  if a user sets `CLAUDE_OTEL_OTLP_HTTP_PORT` directly in their
  shell *and* sets a different value in `appsettings.Local.json`,
  the sidecar's value wins (it overwrites the env var when
  spawning). Document this precedence in Phase 2.

- **Security** — env-var leakage in process listings (`ps`,
  Windows Task Manager → command line) is a known concern but
  the value here is a port number, not a credential. Naming
  with `CLAUDE_OTEL_*` (not `OTEL_*`) keeps us out of the
  OpenTelemetry SDK's reserved namespace, eliminating the risk
  that a future OpenTelemetry SDK release would auto-bind our
  variable. The marker convention's `BR-SECURITY-003` stance
  (never auto-recommend stopping a process we don't own) is
  preserved verbatim.

- **Strategy** — the chosen mechanism (env-var substitution) is
  documented in OTel's `confmap` v1 RFC, so it is unlikely to
  break across collector versions and is recognisable to anyone
  familiar with OTel-Collector deployments. We avoid the lock-in
  cost of mechanism (4) (sidecar-renders-yaml) while preserving
  optionality: if we later need finer-grained overrides, mechanism
  (2) (`--set`) is additive and doesn't conflict with mechanism
  (1). The amended `BR-SKILL-002` (carving read-only judgement
  skills out of the disable-model-invocation rule) sets a
  precedent for future review/report skills (e.g. `/diff-review`,
  `/test-failures-summary`) so the chaining pattern landing here
  is reusable.

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
- Recursive Phase-0 self-bring-up for `/extend-skills` itself
  (offer to chain `/otel up` when `BR-EXTEND-009`'s `/enrich
  plan` step would fail because the collector is down). Future
  plan; would close the meta-gap completely.
- Promoting `RECOVERY_AVAILABLE v1` to a project-wide marker
  catalogue beyond skill-pre-flight use. v1 is skill→skill only.
