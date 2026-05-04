# /demo chains via the Skill tool, not HTTP loopback (P1 fix)

## Motivation

`/demo otel` currently runs its 14-step chain via `ISkillDispatchClient` — sidecar-to-sidecar HTTP loopback inside the .NET process. This bypasses the Claude Code harness entirely:

- No `claude_code.skill_activated` events are emitted for any chained step.
- The chained skills' SKILL.md `!` exec lines, `allowed-tools` matching, and Claude-side rendering are never exercised.
- `BR-DEMO-001`'s "guided onboarding tour AND integration test" promise has never been true. Every reported "passing" `/demo` run since `BR-DEMO-002` landed is a false-green.

This is a P1 breach: every integration validation we have done against `/demo` is invalid. The fix is to invert the dispatch contract — `/demo`'s endpoint emits a structured plan; the demo's SKILL.md body executes the plan via the `Skill` tool inside the live Claude session, producing real `skill_activated` telemetry per chained step.

Two coupled constraints fall out:

- `/demo`'s SKILL.md must NOT use `!` shell-exec preprocessing (the `!` line runs *before* the agent turn — anything it dispatches is invisible to the harness's skill tracking). The plan-fetch and per-step observation calls move into the body as `Bash` tool calls subject to `allowed-tools`.
- `/otel`'s SKILL.md flag `disable-model-invocation: true` blocks the new chain at the `Skill` tool boundary. It flips to `false`. The skill remains user-invocable as it always was; this just additionally permits orchestrator skills (`/demo`, `/extend-skills`) to chain it.

## Files affected

| Path | Change |
|---|---|
| `docs/otel/plans/The-OTEL-Plan-23-demo-skill-tool-chain.md` | This plan file. |
| `src/HelpersSidecar/Domain/IDomainDemo.cs` | Replace single-`RunAsync` shape with an `IDemoTarget` interface exposing `IReadOnlyList<DemoCase> Demos`. Each `DemoCase` carries `Name`, `Description`, `IsDefault`, `Plan` (a `Func` returning `IReadOnlyList<DemoStepDescriptor>`). One default per target. Future plans add per-skill targets and additional cases without touching this contract. |
| `src/HelpersSidecar/Domain/OtelDomainDemo.cs` | Refactor: register one `IDemoTarget` for the OTEL domain with a single `IsDefault=true` case named `happy-path` whose `Plan` returns the existing 14 steps as `DemoStepDescriptor` data — no execution, no `ISkillDispatchClient`. |
| `src/HelpersSidecar/Endpoints/DemoDispatchEndpoint.cs` | Render: pre-flight rows (unchanged), `DEMO_PLAN v1: target="<t>" target_kind="<k>" demo="<d>" steps=<n>` header, numbered `STEP_INVOKE: skill="<name>" args="<argv>" expect="<marker>"` lines, teardown rows. Drop loopback-invocation logic. |
| `src/HelpersSidecar/Endpoints/DemoObserveEndpoint.cs` (new) | `POST /skills/demo/observe` accepts `{run_id, step, pass, detail}`; finalises `DEMO_REPORT v1` to `output/demo-reports/<UTC-ts>-<target>-<demo>.md` when the last step is observed. |
| `src/HelpersSidecar/Infrastructure/ISkillDispatchClient.cs` | Retire — no consumers post-fix. |
| `src/HelpersSidecar/Infrastructure/SkillDispatchClient.cs` | Retire. |
| `src/HelpersSidecar/Program.cs` | Drop `ISkillDispatchClient` DI registration. Register the new `IDemoTarget` for OTEL. Register `DemoObserveEndpoint`. |
| `.claude/skills/demo/SKILL.md` | Remove the `!` exec line entirely. Body becomes pure prose: instruction sequence telling Claude to (1) `curl` the dispatch via the `Bash` tool to fetch the `DEMO_PLAN v1` body, (2) iterate `STEP_INVOKE` markers, invoking each via the `Skill` tool, (3) POST `{run_id, step, pass, detail}` to `/skills/demo/observe` per step. `allowed-tools` becomes `Skill(otel *) Skill(enrich *) Skill(weather *) Skill(skill-bootstrap start *) Bash(curl http://127.0.0.1:5050/skills/demo/dispatch *) Bash(curl http://127.0.0.1:5050/skills/demo/observe *)`. |
| `.claude/skills/otel/SKILL.md` | Flip `disable-model-invocation: true` → `false`. |
| `docs/business-rules.md` | Amend `BR-DEMO-002` (chain via the `Skill` tool, not `ISkillDispatchClient`). Amend `BR-SKILL-007` (orchestrator skills are exempt from the `!` exec requirement; their shell calls live in the agent turn via `Bash`). New `BR-DEMO-007` — every skill named in another skill's `Skill(<name> *)` `allowed-tools` pattern MUST have `disable-model-invocation: false`. New `BR-EXTEND-014` — every registered domain MUST ship at least one `IDemoTarget` whose default case exercises every documented domain action (best-effort exemptions named with one-line reason). New `BR-PROCESS-001` bootstrap-class exception entry covering the `/otel` model-invocation flip. |
| `docs/process-incidents.md` | New incident: "False-green integration-test surface (2026-05-04)". Cites `BR-EXTEND-014` as the response. |
| `tests/HelpersSidecar.Tests/DemoDispatchEndpointTests.cs` | Replace mock-based loopback assertions with assertions on the emitted `DEMO_PLAN v1` shape: header line, one `STEP_INVOKE` per declared step, correct ordering, well-formed `expect` markers. |
| `tests/HelpersSidecar.Tests/DemoObserveEndpointTests.cs` (new) | Asserts incremental observe accumulates correctly and finalises the `DEMO_REPORT v1` markdown when the last step arrives. |
| `tests/HelpersSidecar.Tests/SkillActivatedTelemetryTests.cs` (new) | Phase 4 demo-validation gate test. Reads `output/telemetry.jsonl` after a `/demo otel` Phase 4 run; asserts one `claude_code.skill_activated` event per declared chain step (covers `BR-DEMO-002` amended). |
| `tests/HelpersSidecar.Tests/AllowedToolsModelInvocationTests.cs` (new) | Static scan: every `.claude/skills/*/SKILL.md` whose `allowed-tools` includes `Skill(<name> *)` — assert that `<name>`'s SKILL.md has `disable-model-invocation: false` (covers `BR-DEMO-007`). |

## Behavioural change

**Before.** `/demo otel` invokes `OtelDomainDemo.RunAsync(ctx, ct)` which calls `ISkillDispatchClient.InvokeAsync("otel", "up", ctx)`, `InvokeAsync("otel", "set", ...)`, etc. — each a sidecar-internal HTTP call to `/skills/<name>/dispatch`. The Claude session never invokes the `Skill` tool; the harness emits no `skill_activated` events; `output/telemetry.jsonl` records the collector's own startup telemetry but nothing about the chained steps. The `!` exec line in `demo/SKILL.md` runs once at render time and is the only shell hop.

**After.** `/demo otel` (typed by the user, or invoked via the `Skill` tool in another flow) lands with no `!` preprocessing. The body's first instruction tells Claude to fetch the plan via the `Bash` tool: `curl http://127.0.0.1:5050/skills/demo/dispatch -sS --data-urlencode 'session_id=...' --data-urlencode 'args=otel'`. The response carries `DEMO_PLAN v1: target="otel" target_kind="domain" demo="happy-path" steps=14` followed by 14 numbered `STEP_INVOKE: skill="<name>" args="<argv>" expect="<marker>"` lines. Claude iterates them in order; for each step, invokes the named skill via the `Skill` tool with the given args, captures the response, and POSTs `{run_id, step, pass, detail}` to `/skills/demo/observe`. Each `Skill` tool invocation traverses the real harness path: `claude_code.skill_activated` fires, the chained skill's SKILL.md `!` exec runs (where present), `allowed-tools` matches, and Claude renders the response. `output/telemetry.jsonl` accumulates one `skill_activated` event per chain step. The final `observe` POST triggers `DemoObserveEndpoint` to flush `DEMO_REPORT v1` to `output/demo-reports/<UTC-ts>-otel-happy-path.md`.

## Test approach

**Business rules satisfied / amended:**

- `BR-DEMO-001` — guided onboarding + integration test. The integration-test half is now actually true. No new test (the existing dispatch-shape test covers the onboarding half); the integration-test half is covered by the new telemetry-assertion test below.
- `BR-DEMO-002` — amended: chain via the `Skill` tool, not `ISkillDispatchClient`. New test `[Fact(DisplayName = "BR-DEMO-002 — chained steps emit skill_activated events")]` in `SkillActivatedTelemetryTests`. Reads `output/telemetry.jsonl` after a Phase 4 `/demo otel` run; asserts exactly one `claude_code.skill_activated` event per `STEP_INVOKE` declared in the plan; asserts event ordering matches plan ordering.
- `BR-DEMO-004` — `DEMO_REPORT v1`. New test `[Fact(DisplayName = "BR-DEMO-004 — observe finalises report file")]` in `DemoObserveEndpointTests`. Submits N observes for an N-step plan; asserts the report file exists, contains every step's pass/detail, and is named per `BR-DEMO-004` schema.
- `BR-DEMO-007` (new) — chained skills must be model-invocable. New test `[Fact(DisplayName = "BR-DEMO-007 — Skill(<name>) targets are model-invocable")]` in `AllowedToolsModelInvocationTests`. Static scan over `.claude/skills/*/SKILL.md`.
- `BR-EXTEND-014` (new) — every domain ships an `IDemoTarget` covering every action. New test `[Fact(DisplayName = "BR-EXTEND-014 — every registered IDomain has IDemoTarget covering every action")]`. Iterates `IEnumerable<IDomain>`, requires a corresponding `IDemoTarget` registration with at least one `DemoCase`, requires the default case's `STEP_INVOKE` set to cover every verb in the domain's skill surface (best-effort exemptions read from a `[DemoExemption]` attribute or equivalent — text TBD at Phase 2).
- `BR-SKILL-007` — amended: orchestrator skills are exempt from the `!` exec requirement. No new test (the rule is positive about non-orchestrators, exempting orchestrators). The amended text is covered by `AllowedToolsModelInvocationTests` indirectly (the demo skill's tool list won't match an `!`-exec-only pattern).

**Existing tests:**

- `DemoDispatchEndpointTests` — refactored, not deleted. Old assertions on `ISkillDispatchClient.Received` deleted. New assertions verify the `DEMO_PLAN v1` shape.
- All other unit/integration suites unchanged; this plan touches only the demo flow's wire shape.

**Phase 4 demo-validation gate (`BR-EXTEND-012`):**

- `/demo otel` runs through the new path end-to-end. `DEMO RESULT` line must read `14/14 PASS`. The `SkillActivatedTelemetryTests` test, run as part of Phase 4's regular suite, verifies the telemetry side-effect.

## Architecture review decisions

> Phase 1.5 fast-tracked under explicit user override (P1 breach: every prior `/demo` run was a false-green integration test; fixing the harness-traversal gap took precedence over the formal review cycle). Resolutions recorded inline below per `BR-PROCESS-009`'s "Override" branch. Each resolution carries a one-line justification per the schema.

- BR-DEMO-002 (chain via `ISkillDispatchClient` HTTP loopback → chain via the `Skill` tool in the live agent turn): **Resolution: Evolve** — the loopback was the architectural defect being fixed; the rule's intent ("integration test exercising the entire skill stack") is preserved while the mechanism becomes the only one that actually traverses the harness.
- BR-SKILL-007 (skills MUST be pure markdown + a single `!` shell invocation → orchestrator skills are exempt from the `!` requirement): **Resolution: Evolve** — orchestrators run in the agent turn rather than at render time; their shell calls live behind the `Bash` tool subject to `allowed-tools`, not in `!` exec which fires before the agent can see anything.
- BR-PROCESS-001 (bootstrap-class exception covering the `/otel` `disable-model-invocation: true → false` flip): **Resolution: Override** — the `Skill`-tool-chain architecture introduced by this plan didn't exist when the flag was set; the same exception shape used for `/extend-skills` and `/skill-bootstrap` applies here.

## Rollback steps

If the change has to be reverted after landing:

1. `git revert <feat-commit-sha>` — restores `ISkillDispatchClient`, the loopback invocation in `OtelDomainDemo`, the `!` exec line in `demo/SKILL.md`, and `disable-model-invocation: true` on `/otel`.
2. `git revert <chore-commit-sha>` — if the build commit included regenerated artefacts.
3. `git revert <test-commit-sha>` — restores prior test assertions and removes the new telemetry / observe / model-invocation tests.
4. **Manual sanity check post-revert:** run `/demo otel`; expect zero `claude_code.skill_activated` events in `output/telemetry.jsonl` for the chain steps (confirming we are back on the broken-but-currently-shipping surface). If the count is non-zero post-revert, something else changed in the interim and needs investigation.

The plan-commit itself can be reverted independently with `git revert <plan-commit-sha>` if we want to remove the plan file from history.

## Out of scope

- **Per-skill demos** (`/demo enrich`, `/demo weather`, `/demo domain-info`, etc.). The `IDemoTarget` contract that lands here supports them; the registrations land in a future plan.
- **Multiple OTEL demo cases** beyond `happy-path`. The collection-shape lands here (one case for now); additional cases (`enrichment-only`, `lifecycle`, `recovery-offer`) land in a future plan.
- **Container-mode collector port publishing** (a separate defect surfaced in the same chat thread — `DockerCli.RunDetachedAsync` only `-p`-maps `127.0.0.1:{hostPort}:5050`; the collector's three ports are never published). Distinct concern, distinct revert. A separate plan after this one lands.
- **Retroactive evidence-counter reset** across `docs/retros.md`. Done as a `docs:` commit after this plan lands; not part of the gated phases.
- **Generalisation to `IDemoTarget` beyond domains** (i.e. `ISkillDemo` for individual skills as `/demo` targets). The interface is named `IDemoTarget` so the future expansion is non-breaking; the registration of skill-level targets is a future plan.
