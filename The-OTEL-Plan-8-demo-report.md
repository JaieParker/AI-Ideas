# Demo report — durable, human-readable artefact correlating each step with the OTEL records it emitted

> Plan-8 follows Plan-5 (`8ede7fb`–`7cba5ae` — domain interface +
> rename + IDomainDemo + /domain-info), Plan-6 (`c2b0ce3` —
> drafted, unimplemented), and Plan-7 (`eb579a0` — drafted,
> unimplemented). Plan-8 closes the "too fast for humans" gap
> in `/demo`: a durable markdown report at
> `output/demo-reports/<UTC-timestamp>-<domain>.md` correlating
> each step with the OTEL records it produced, plus a meta-rule
> (`BR-PROCESS-013`) that names the pattern across Plans 6, 7, 8.
> Commit prefixes follow `BR-EXTEND-002`.
> Order in the queue: **Plan-6 → Plan-8 → Plan-7**.

## Motivation

Three connected gaps in `/demo` today:

1. **Demo runs are too fast for humans to follow.** A complete
   run finishes in ~2 seconds. Every step's pass/fail is
   reported but with no timing, so a human can't see which step
   took how long, which step's chain was slow, or where time
   went.

2. **OTEL records emitted during the demo are invisible to the
   user.** The collector writes records to
   `output/telemetry.jsonl` correctly, but `/demo`'s response
   only mentions JSONL as a count (`5 records, 1024 bytes`).
   The records themselves — the actual evidence the demo
   produces — go into the file unannotated. To inspect, the
   user opens a separate file and tries to correlate timestamps
   by hand.

3. **The demo run is ephemeral.** The response is text in this
   Claude turn. Once the session closes, the demo evidence is
   lost. Re-running gives a *new* demo, not a record of *the*
   demo. There's no artefact to share with a teammate, link
   from a retro, or attach to a PR.

Plan-8 closes all three by writing a durable markdown report
that correlates each step with the OTEL records it emitted,
times every step, and persists at a known path.

`Plan-8` also codifies the broader pattern Plans 6, 7, and 8
all follow as `BR-PROCESS-013` — every multi-step lifecycle
event in this project produces a durable report.

## New / changed business rules

- **`BR-DEMO-004` — Demo runs produce a durable human-readable
  report correlating each step with the OTEL records it
  emitted.** Every `/demo <domain>` invocation MUST write
  `output/demo-reports/<UTC-timestamp>-<domain>.md` containing:

  1. **Header** — timestamp, domain, session id, `plan` enrichment
     value, total elapsed.
  2. **Pre-flight table** — `STEP 00.x` rows with per-row elapsed
     times.
  3. **Live demo steps** — for each step:
     - Step number, label, PASS|FAIL, started-at, elapsed.
     - `chain →` detail (same as console).
     - **OTEL records produced during this step**: every
       `output/telemetry.jsonl` record whose timestamp ∈
       `[step.started_at, step.ended_at]` AND whose
       `session.id` (or `plan` enrichment) matches the demo's
       session.
  4. **Final summary** — `DEMO RESULT: x/y PASS`.
  5. **Teardown** — same instructions the console emits.
  6. **Appendix** — full JSONL slice for the demo run window
     (records that don't fall inside any single step's window
     land here, not in the per-step sections).

  The console response gains exactly one line at the end:
  `Report saved to: output/demo-reports/<timestamp>-<domain>.md`.

  `--no-report` (or equivalent) opts out for fast-loop test
  scenarios per `BR-PROCESS-007`.

  **Why:** without correlation between steps and OTEL records,
  the demo's evidence value is half-complete. The report makes
  every demo run a shareable artefact and turns "a run from
  Tuesday" into something a contributor can actually review.

- **`BR-PROCESS-013` — Multi-step lifecycle events produce a
  durable, human-readable, schema-versioned report.** Every
  multi-step lifecycle operation that a human might want to
  review later MUST produce a markdown report at a documented
  path with a versioned schema. Examples (all introduced in
  Plans 6, 7, 8):

  | event              | report path                                                  | schema version |
  |--------------------|--------------------------------------------------------------|----------------|
  | demo run           | `output/demo-reports/<ts>-<domain>.md`                       | DEMO_REPORT v1 |
  | promote attempt    | `output/promote-reports/<ts>-<component>.md` *(Plan-7)*      | PROMOTE_REPORT v1 |
  | architecture review| `output/architecture-reviews/<ts>-<plan>.md` *(Plan-6)*      | ARCHITECTURE_REVIEW v1 |

  The schema-version line at the top of each report lets future
  schema changes increment the version while keeping older
  reports parseable. Reports are NOT edited in-place after
  creation; cleanup verbs may delete them, but content is
  immutable from the writer's perspective.

  This rule **names the pattern** Plans 6, 7, and 8 each
  individually adopt. Per `BR-PROCESS-005`'s evidence rule, the
  rule lands at the third concrete example (this plan) — not
  earlier.

  **Why:** the project's audit trail is currently a mix of
  commits, BR text, and `process-incidents.md` entries. Adding
  durable per-event reports makes the *transient* events (a
  demo run, a promote, a review) auditable too. Schema
  versioning makes the audit trail durable across project
  generations.

## Files affected

### New files

| Path | Purpose |
|---|---|
| `src/HelpersSidecar/Domain/IDemoReportWriter.cs` | Contract — writes a markdown report from preflight rows + step results + JSONL slice. |
| `src/HelpersSidecar/Domain/MarkdownDemoReportWriter.cs` | Concrete implementation — produces the BR-DEMO-004 layout. |
| `src/HelpersSidecar/Infrastructure/JsonlSliceReader.cs` | Reads `output/telemetry.jsonl`, filters by timestamp window + session id, returns a structured slice. Wraps `BR-DEMO-003`'s `FileShare.ReadWrite|Delete` pattern. |
| `tests/HelpersSidecar.Tests/Domain/MarkdownDemoReportWriterTests.cs` | NEW — covers the BR-DEMO-004 layout (header, per-step sections with OTEL records, summary, teardown, appendix). Mocks `JsonlSliceReader`. |
| `tests/HelpersSidecar.Tests/Infrastructure/JsonlSliceReaderTests.cs` | NEW — covers timestamp-window + session filtering, file-share semantics, malformed-record skipping. |

### Modified files

| Path | Change |
|---|---|
| `src/HelpersSidecar/Domain/IDomainDemo.cs` | `DemoStepResult` gains `StartedAt` and `EndedAt` (`DateTimeOffset`). The change is additive (defaults via constructor); existing producers update to capture timestamps. |
| `src/HelpersSidecar/Domain/OtelDomainDemo.cs` | Each step helper captures `DateTimeOffset.UtcNow` before and after the await, populates `StartedAt`/`EndedAt`. |
| `src/HelpersSidecar/Endpoints/DemoDispatchEndpoint.cs` | After `IDomainDemo.RunAsync`, calls `IDemoReportWriter.WriteAsync` with preflight + step results + JSONL slice. Appends `Report saved to: <path>` to the console response. Honours `--no-report` if the args contain it. |
| `src/HelpersSidecar/Program.cs` | DI for `IDemoReportWriter`, `JsonlSliceReader`. |
| `tests/HelpersSidecar.Tests/Endpoints/DemoDispatchEndpointTests.cs` | Test asserts the `Report saved to:` line is present in the response (and the writer was called once). Uses a fake `IDemoReportWriter`. |
| `tests/HelpersSidecar.Tests/Domain/OtelDomainDemoTests.cs` | New assertion that every `DemoStepResult` has `StartedAt < EndedAt` and both are within the test's wall-clock window. |
| `docs/business-rules.md` | Add `BR-DEMO-004` and `BR-PROCESS-013`. Cross-reference Plan-6 and Plan-7 to receive `BR-PROCESS-013` references in their respective implementation phases. |
| `CLAUDE.md` | Add a one-paragraph "Lifecycle reports" section under "Architecture summary"; cross-link from the existing demo-skill description. |
| `docs/process-incidents.md` | Append entry — "Demo evidence was ephemeral; Plan-8 closes the gap by making demo runs durable artefacts". |
| `.gitignore` | `output/demo-reports/` is intentionally NOT ignored — these are committed evidence. (If we change our minds on retention, gitignore in a follow-up; default is "every report is preserved".) |

## Behavioural change

**Before:**

```
$ /demo otel
=== /demo — guided tour of the otel domain ===
PRE-FLIGHT
STEP 00.a: PASS — Helpers sidecar...
...
DEMO RESULT: 14/14 PASS
```

The output is rendered into the Claude session and lost when
the session closes. Per-step timing is invisible. OTEL records
in `telemetry.jsonl` are uncorrelated with steps.

**After:**

```
$ /demo otel
=== /demo — guided tour of the otel domain ===
PRE-FLIGHT
STEP 00.a: PASS — Helpers sidecar... (12 ms)
...
DEMO RESULT: 14/14 PASS
Report saved to: output/demo-reports/20260503T084217Z-otel.md
```

The console output is roughly the same shape but per-step
timing is now visible. The durable file at
`output/demo-reports/20260503T084217Z-otel.md` carries the
full report including per-step OTEL excerpts and the JSONL
appendix — see the BR-DEMO-004 spec for the layout.

## Test approach

Per `BR-PROCESS-007` every test scopes to one domain change:

- **`MarkdownDemoReportWriterTests`** — verifies the BR-DEMO-004
  layout. Hand-construct preflight + step results + a stub
  `JsonlSliceReader` returning canned records. Assert: header
  has timestamp + domain + session id + plan + total elapsed;
  every step has its records section populated correctly;
  records that fall outside any step window land in the
  appendix; schema version line is `DEMO_REPORT v1`.
- **`JsonlSliceReaderTests`** — timestamp-window filtering;
  session-id filtering; `plan` enrichment as alternative
  filter; file-share semantics (`BR-DEMO-003`'s
  `FileShare.ReadWrite|Delete` pattern); malformed JSONL line
  is skipped, not fatal.
- **`DemoDispatchEndpointTests`** (modified) — adds an
  assertion that the response includes
  `Report saved to: ...` exactly once and that the
  `IDemoReportWriter` mock was called once with the expected
  arguments. The `--no-report` opt-out path tested by passing
  the arg and asserting the writer was NOT called.
- **`OtelDomainDemoTests`** (modified) — every step's
  `StartedAt < EndedAt` and both are non-default.

End-to-end runtime acceptance: run `/demo otel`, open the
generated `output/demo-reports/<ts>-otel.md`, confirm:

- The 14 live-step sections each cite at least one OTEL record
  (assuming the collector is up and writing JSONL).
- The appendix contains the full demo-window JSONL slice.
- Times are in monotonic order.

## Phase ordering

1. **Phase 1 (this commit)** — plan file. `plan:` prefix.

2. **Phase 2a** — `DemoStepResult` gains `StartedAt`/`EndedAt`.
   `OtelDomainDemo` updates each step helper to capture the
   timestamps. Test for monotonicity. `feat(otel):` prefix.

3. **Phase 2b** — `JsonlSliceReader` + tests. Pure infrastructure;
   no consumers wired yet. `feat(otel):` prefix.

4. **Phase 2c** — `IDemoReportWriter` + `MarkdownDemoReportWriter`
   + tests. The writer takes preflight + steps + JSONL slice
   and produces the BR-DEMO-004 markdown. `feat(otel):` prefix.

5. **Phase 2d** — `DemoDispatchEndpoint` integrates the writer;
   appends `Report saved to: <path>` to console; honours
   `--no-report`. `refactor(otel):` prefix.

6. **Phase 2e** — `BR-DEMO-004` and `BR-PROCESS-013` land in
   `docs/business-rules.md`. CLAUDE.md "Lifecycle reports"
   section. `process-incidents.md` entry. `docs:` prefix.

7. **Phase 3 — Build.** `chore:` prefix only if artefacts
   changed.

8. **Phase 4 — Test.** Full suite + a manual `/demo otel` run
   to inspect a real generated report. `test:` prefix.

9. **Phase 5 (acceptance)** — share the generated report with
   the user; confirm it answers "what happened in this demo"
   without further questions.

## Rollback

Each phase commits separately:

1. `git revert <plan-commit>` — drops Plan-8.
2. `git revert <2a..2e>` (in reverse order) — removes report
   writing. `/demo` reverts to the pre-Plan-8 console-only
   shape. Existing reports in `output/demo-reports/` stay
   (they're durable evidence; the rollback removes future
   writing, not past reports).
3. `git revert <3-chore>` / `<4-test>` as needed.

## Out of scope

- **Architecture-review reports** — Plan-6 will produce them;
  the *path*, *schema*, and *retention* follow `BR-PROCESS-013`
  but the implementation is Plan-6's, not Plan-8's. Plan-8
  cross-references; doesn't implement.
- **Promote reports** — Plan-7 will produce them; same
  cross-reference relationship.
- **Cleanup verb for old reports** — `output/demo-reports/`
  grows monotonically until a future plan adds a `--cleanup`
  verb. v1 keeps every report; storage is small (~20-50 KB per
  report).
- **CI integration of report generation** — running `/demo` in
  CI to produce a "build-N report" is a real future use-case,
  but Plan-8 doesn't add CI plumbing. The reports are local
  artefacts.
- **Cross-domain report aggregation** — when kai-platform
  lands, its `/demo kai-platform` will produce its own report.
  An aggregate "all domains demo digest" report is a future
  plan if/when needed.
- **Retroactive reports for completed demos** — there's no
  log to reconstruct from. New demos produce reports going
  forward.

## What this earns Plan-7 + Plan-6

`BR-PROCESS-013` is the meta-rule that gives Plan-7's
promote-reports and Plan-6's architecture-review records a
shared shape. When Plan-7 implements promote, the
`PROMOTE_REPORT v1` follows BR-PROCESS-013's structure (header,
per-step section with timing + outcome + relevant evidence,
summary, schema version line). Same for `ARCHITECTURE_REVIEW v1`
in Plan-6.

`MarkdownDemoReportWriter` is shaped generically enough that
its core "render markdown from a (header, sections, appendix)
triple" logic could be extracted into an `IMarkdownReportWriter`
shared by all three plans. Plan-8 doesn't extract proactively —
the third example will reveal what shape the abstraction
actually wants.

## What kai-platform inherits

When `KaiPlatformDomain` registers a `KaiPlatformDomainDemo`
(per `BR-EXTEND-010`), `/demo kai-platform` automatically
produces a report at `output/demo-reports/<ts>-kai-platform.md`
following the same BR-DEMO-004 layout. Zero changes in `/demo`
or the report writer.

## Schema version line

Every report's first line after the title is:

```markdown
# Demo report — <domain>
DEMO_REPORT v1

- Generated: ...
- Session id: ...
```

The `<NAME> v<N>` convention parallels Plan-6's
`ARCHITECTURE_REVIEW v1` and Plan-7's prospective
`PROMOTE_REPORT v1`. Schema changes increment the version;
older reports keep their original version marker for
backward-compatibility parsing.
