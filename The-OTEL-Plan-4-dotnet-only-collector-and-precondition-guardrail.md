# .NET-only collector + skill-bootstrap precondition guardrail + /demo as guided onboarding

> Plan-4 produced after the bootstrap-exception commit
> `bb3398c` (which built `/skill-bootstrap`). This plan covers
> every phase that runs through `/otel-extend` from here.

## Motivation

`/demo` is meant to be (a) a one-command onboarding for a new user
and (b) the project's end-to-end integration test. Today it does
neither: every skill in the project — `/demo` included —
dispatches via the helpers sidecar at `127.0.0.1:5050` with no
precondition probe, so when the sidecar is down (the natural state
on a clean machine) the `!` exec aborts with `curl: (7)` and the
skill body never reaches Claude. The user sees an opaque shell
error instead of any usable next step.

The bootstrap-exception commit `bb3398c` introduced
`/skill-bootstrap` to fix the chicken-and-egg. This plan adds the
remaining work to deliver the success criterion:

> `/demo`, run on a clean machine, shows everything as off and not
> installed; tells the user how to turn it on; demonstrates the
> install/configure/teardown story end-to-end.

In parallel, the plan pivots the collector from Go-via-OCB to a
.NET project (`src/Collector/`), simplifying the platform's
language story to .NET-only. The pivot is justified separately in
the 2026-05-03 entry of `docs/process-incidents.md`; the
TL;DR is that we use only two tiny components from the OTel Go
collector framework (`otlpreceiver`, `fileexporter`), and
re-implementing them in .NET removes Go from the project's
build path while preserving the option to chain out to OCB at
runtime if a contrib exporter is ever needed.

## Files affected

| Path | Change |
|---|---|
| `src/Collector/Collector.csproj` | NEW — .NET 10 minimal API project for the collector |
| `src/Collector/Program.cs` | NEW — listener wiring (4318, 13133, 13134), exporter, processor pipeline |
| `src/Collector/Receivers/OtlpHttpReceiver.cs` | NEW — accepts OTLP/HTTP on `:4318`, parses protobuf via `OpenTelemetry.Proto` |
| `src/Collector/Processors/PerSessionEnrichmentProcessor.cs` | NEW — stamps per-session attributes |
| `src/Collector/Processors/CollectionToggleFilter.cs` | NEW — drops batches by `session.id` per BR-ENRICH-004 |
| `src/Collector/Processors/PersistentEnrichmentsProcessor.cs` | NEW — stamps persistent attributes |
| `src/Collector/Exporters/JsonlFileExporter.cs` | NEW — writes records to `output/telemetry.jsonl` |
| `src/Collector/Endpoints/ControlEndpoints.cs` | NEW — `:13133` HTTP API for sessions/enrichments/persistent/control |
| `src/Collector/Endpoints/HealthzEndpoint.cs` | NEW — `:13134/healthz` |
| `tests/Collector.Tests/**` | NEW — port BR-ENRICH-* and BR-OTEL-* tests from Go |
| `OTEL.slnx` | EDIT — add Collector + Collector.Tests to the solution |
| `tools/legacy/go-collector/**` | NEW — moved from previous Go collector location |
| `tools/otel-collector` | DELETE — no longer in primary build path |
| `.claude/skills/skill-bootstrap/SKILL.md` | EDIT — extend to probe/build/start/stop the collector tier as well as the sidecar tier |
| `.claude/skills/skill-bootstrap/HELP.md` | EDIT — document collector tier verbs |
| `.claude/skills/otel/SKILL.md` | EDIT — add `\|\| printf 'PRECONDITION_FAIL: ... /skill-bootstrap ...'` fallback to the `!` line |
| `.claude/skills/enrich/SKILL.md` | EDIT — same fallback |
| `.claude/skills/weather/SKILL.md` | EDIT — same fallback |
| `.claude/skills/otel-extend/SKILL.md` | EDIT — same fallback |
| `.claude/skills/demo/SKILL.md` | EDIT — same fallback + restructured body for "off-state-first" tour |
| `src/HelpersSidecar/Endpoints/DemoDispatchEndpoint.cs` | EDIT — emits `STEP NN: PASS|FAIL — ...` markers and `DEMO RESULT: x/y PASS`; preflight section that probes everything; teardown section |
| `tests/HelpersSidecar.Tests/Endpoints/DemoDispatchEndpointTests.cs` | EDIT — assertions on the new marker format |
| `tests/HelpersSidecar.Tests/Endpoints/DemoIntegrationTests.cs` | NEW — integration test driven by the dispatch endpoint, asserts every step's marker |
| `tests/HelpersSidecar.Tests/SkillConventions/SkillPreconditionLintTests.cs` | NEW — BR-SKILL-010 lint test |
| `docs/business-rules.md` | EDIT — add BR-SKILL-010, BR-DEMO-001, BR-COLLECTOR-* (new area for the collector tier) |
| `CLAUDE.md` | EDIT — remove Go from BR-SKILL-008's accepted-dependency list; update architecture summary table; document chain-out option |

## Behavioural change

**Before:**
- Every dispatching skill curls the sidecar with no fallback. If the sidecar is down, the skill produces a raw `curl: (7)` shell error.
- `/demo` runs only when the full stack (sidecar + collector + JSONL output) is already up. On a clean machine it is unreachable.
- The collector is a Go binary built via OCB. The project requires a Go toolchain plus OCB plus the upstream collector framework dependencies.
- Onboarding is undocumented; the user is expected to have already read the README quickstart (which doesn't exist).

**After:**
- Every dispatching skill probes `:5050` first; on failure the user sees a `PRECONDITION_FAIL` line with a clear next-step (`/skill-bootstrap status` then `/skill-bootstrap start`).
- `/skill-bootstrap` covers both the helpers-sidecar tier and the collector tier (collector tier now also .NET).
- `/demo` is a guided tour: it probes everything, prints a structured PASS/FAIL block honest about the off-state, then walks install / start / configure / re-run / teardown commands. The same skill doubles as the project's integration test (every step has a stable marker).
- The project is .NET-only at build level. Go disappears from `BR-SKILL-008`'s accepted-dependency list.

## Test approach

Business rules added in this plan and the tests that prove them:

- **BR-SKILL-010** — every dispatching skill's `!` exec line MUST end with `|| printf 'PRECONDITION_FAIL: ... /skill-bootstrap ...'`. `/skill-bootstrap` is the single named exemption. Test: `SkillPreconditionLintTests.cs` walks every `.claude/skills/*/SKILL.md` and asserts the convention.
- **BR-DEMO-001** — `/demo` MUST emit `STEP NN: PASS|FAIL — ...` for every step plus a `DEMO RESULT: x/y PASS` summary, AND MUST emit install/start instructions when its preconditions are not met. Test: `DemoIntegrationTests.cs` runs the dispatch endpoint with a stubbed collector and asserts every marker.
- **BR-COLLECTOR-001** through **BR-COLLECTOR-NN** — port of the Go-side BR-ENRICH-* and BR-OTEL-* invariants to the .NET collector. Existing test names transfer; only the implementation under test changes.

The test `display name` rule from CLAUDE.md applies to every new test: name starts with the BR ID.

## Rollback steps

Each phase commits separately so any phase can be reverted in
isolation:

1. `git revert <2a-feat-commit>` — reverts the .NET collector pivot, restores the Go collector reference (which was *moved* not deleted in this plan, so the revert is clean).
2. `git revert <2b-feat-commit>` — reverts the precondition guardrail changes across SKILL.md files and the lint test.
3. `git revert <2c-feat-commit>` — reverts the `/demo` restructure.
4. `git revert <2d-feat-commit>` — reverts the `/skill-bootstrap` collector-tier extension.
5. `git revert <3-chore-commit>` — reverts rebuilt artefacts.
6. `git revert <4-test-commit>` — reverts the test pass marker (rare).

The bootstrap-exception commit (`bb3398c`) is intentionally NOT
covered by this rollback story — it must remain as the only path
through which the sidecar can be brought up.

## Out of scope

- **Auto-installing the .NET SDK or the Go runtime.** `BR-SECURITY-003` forbids it. `/skill-bootstrap install` will continue to print install links and stop on a missing SDK.
- **Running the actual OCB chain-out.** The .NET collector exposes a pluggable exporter interface; the `OtlpHttpExporter` for chain-out to a downstream OCB binary is documented as a v2 capability but not implemented in this plan.
- **A migration tool** for users with existing `output/telemetry.jsonl` files written by the Go collector. The two collectors produce the same JSONL shape (we'll confirm via the BR-OTEL ports); no migration is needed.
- **Removing the `Bash(go *)` permission from `/otel-extend`'s `allowed-tools`.** Tightening that pattern is a separate change, captured as a follow-up task.

## Phase ordering

1. **Phase 1 (this commit)** — plan file. `plan:` prefix.
2. **Phase 2a** — .NET collector under `src/Collector/`, ported tests, solution wiring, Go collector moved to `tools/legacy/`. `feat(otel):` prefix.
3. **Phase 2b** — probe-or-instruct fallback across all dispatching skills + `BR-SKILL-010` lint test. `feat(skills):` prefix.
4. **Phase 2c** — `/demo` restructure (PASS/FAIL markers, off-state-first, teardown) + `BR-DEMO-001` integration test. `feat(otel):` prefix.
5. **Phase 2d** — `/skill-bootstrap` extended to manage collector tier (probe/build/start/stop). `feat(skill-bootstrap):` prefix.
6. **Phase 2e** — CLAUDE.md / `BR-SKILL-008` updated to remove Go from the accepted-dependency list. `docs:` prefix.
7. **Phase 3** — `dotnet build` for the new solution; `chore:` prefix if artefacts changed.
8. **Phase 4** — `dotnet test` full suite; `test:` prefix.
9. **Phase 5 (acceptance)** — run `/demo` with the sidecar (and optionally the collector) toggled off to verify the success criterion.
