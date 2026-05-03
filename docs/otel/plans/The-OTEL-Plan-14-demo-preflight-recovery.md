# Plan-14 — `/demo` self-recovers when the sidecar is down

## Motivation

Today, running `/demo otel` against a stopped sidecar prints
`PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on
127.0.0.1:5050` and stops. The user is then told to type
`/skill-bootstrap status` and `/skill-bootstrap start`
themselves. That's wrong: `/demo` exists precisely so a new user
can experience the platform end-to-end — handing them a chain
of commands defeats the purpose.

`BR-SKILL-014` already specifies the `RECOVERY_AVAILABLE v1`
offer-then-chain pattern for exactly this situation: a skill
detects an auto-recoverable down-state, surfaces the marker, the
user gives one explicit "yes" (HITL), the skill chains to the
recovery skill via the `Skill` tool. This plan wires `/demo` (and
`/extend-skills`, which shares the same dispatch dependency) to
emit the marker, and removes the structural blockers that
prevent the chain from working.

The structural blockers — uncovered while bootstrapping this
plan — are:

1. `/skill-bootstrap` was `disable-model-invocation: true`, so
   Claude could not chain into it from `/demo`. Fixed in
   bootstrap-class commit `c2aca79` (BR-PROCESS-001 exception
   #3) ahead of this plan; this plan ratifies that commit.
2. `/enrich` was `disable-model-invocation: true` and described
   as "User-only", which broke the BR-EXTEND-009 plan-tag chain
   in `/extend-skills` and any future skill-to-skill enrichment.
   Per the project's design intent, `/enrich` is meant to be
   chained from other skills so they can tag the OTEL data they
   produce. This plan flips the flag to `false` and revises the
   description to match.

## Files affected

| Path | Change |
|---|---|
| `.claude/skills/skill-bootstrap/SKILL.md` | (Already changed in `c2aca79`.) `disable-model-invocation: true` → `false`. Plan-14 ratifies this as bootstrap exception #3. |
| `.claude/skills/enrich/SKILL.md` | `disable-model-invocation: true` → `false`. Description: drop the "User-only — only the human types /enrich" wording; replace with "Chainable from other skills so they can tag the OTEL data they produce." Validation rules unchanged. |
| `.claude/skills/demo/SKILL.md` | Replace the single dispatch `!` exec with a layered probe: probe `:5050/healthz` first; on `SIDECAR_DOWN` emit `RECOVERY_AVAILABLE v1: skill="skill-bootstrap" verb="start" reason="…"`; suppress the marker if `:5050` is held by a non-project process (`BR-SECURITY-003`). On healthz OK, run the existing dispatch curl as today. Add `Skill(skill-bootstrap start *)` to `allowed-tools`. |
| `.claude/skills/extend-skills/SKILL.md` | Same layered probe — `/extend-skills` has the same dispatch dependency. Add `Skill(skill-bootstrap start *)` and `Skill(enrich plan *)` to `allowed-tools`. The Phase-0 instruction body changes from "tell the user to run `/enrich plan <filename>`" to "Claude invokes `/enrich plan <filename>` via the Skill tool". |
| `docs/business-rules.md` | New `BR-DEMO-005` (demo recovery offer + foreign-process suppression). Append `c2aca79` (and the new `/enrich` flip commit) to the `BR-PROCESS-001` exceptions list — both are bootstrap-class. |
| `docs/process-incidents.md` | Append a short incident entry capturing the chicken-and-egg discovered while bootstrapping Plan-14 (the `/skill-bootstrap` and `/enrich` invocability blockers, plus how the recovery design wires around them in future). |
| `tests/HelpersSidecar.Tests/Demo/DemoPreflightRecoveryTests.cs` | New integration tests for `BR-DEMO-005`: (a) sidecar down → marker present, (b) `:5050` held by foreign PID → marker suppressed, (c) post-recovery dispatch returns the live demo chain. |
| `tests/HelpersSidecar.Tests/ExtendSkills/PlanTagEnrichmentChainTests.cs` | New test confirming `/extend-skills` Phase 0 chain-invokes `/enrich plan <filename>` (and that the `enrich` skill's frontmatter permits it). |

## Behavioural change

**Before:**

- `/demo otel` against a down sidecar → `PRECONDITION_FAIL`,
  user must run `/skill-bootstrap start` themselves.
- `/extend-skills otel <topic>` against a down sidecar → same.
- `/extend-skills` Phase 0 emits `PLAN_TAG_ENRICHMENT: /enrich
  plan <name>` which only the human can run; the chain breaks.

**After:**

- `/demo otel` against a down sidecar → `RECOVERY_AVAILABLE v1`
  marker shown to the user with the reason. User says "yes";
  Claude invokes `/skill-bootstrap start` via the `Skill` tool.
  On success, Claude re-runs `/demo otel`. The user typed one
  command total: `/demo otel`.
- Same flow for `/extend-skills`.
- `/extend-skills` Phase 0 chain-invokes `/enrich plan
  <filename>` automatically — no user typing required. OTEL
  records from the flow are tagged with the plan name from the
  next OTLP flush onward.
- If `:5050` is held by a process that isn't ours
  (`Conflict` lifecycle state), the marker is suppressed and
  the user is told to investigate manually — `BR-SECURITY-003`
  forbids us recommending we kill a foreign process.

## Test approach

New BR `BR-DEMO-005`: "When the sidecar is down and `:5050` is
free or owned by a stale PID file, `/demo` and `/extend-skills`
emit `RECOVERY_AVAILABLE v1` pointing at `/skill-bootstrap
start`. When `:5050` is held by a foreign process, the marker
is suppressed and the user gets the manual-investigation
message." The new integration tests in
`DemoPreflightRecoveryTests.cs` cover both arms.

`BR-EXTEND-009` (existing) gets a new test that asserts
Phase 0 of `/extend-skills` produces a Skill-tool invocation of
`/enrich plan <filename>` rather than a printed instruction
for the user. Covered by `PlanTagEnrichmentChainTests.cs`.

`BR-PROCESS-001`'s exception list grows by two entries — the
existing `c2aca79` for `/skill-bootstrap`, and the new commit
that flips `/enrich`. Both are mechanically checked by the
existing `BR-PROCESS-001` test (filename + commit-message
audit) once documented in `docs/business-rules.md`.

## Architecture review decisions

> BR-PROCESS-009 gate. After Phase 1's plan-file commit,
> `/architecture-review <plan-file>` produces a structured
> review. For each `ARCHITECTURE_DECISION_REQUIRED` block in
> the review output, record the user's resolution here. Phase 2
> (Implement) does NOT proceed until every commitment named by
> the review has a matching line below.

- **BR-DOCS-AREA-LIST** (CLAUDE.md areas list): **Resolution: Evolve** — add "DEMO" to the allow-list with the one-line rationale that the demo flow is a distinct onboarding surface owning its own report artefact (BR-DEMO-004) and now its own recovery contract (BR-DEMO-005); coupling to SKILL would dilute the SKILL area's meaning. CLAUDE.md and `docs/business-rules.md` updated in Phase 2.

## Rollback steps

If the change has to be reverted after landing, the rollback is:

1. `git revert <feat-commit-sha>` (filled in after Phase 2)
2. `git revert <chore-commit-sha>` (filled in after Phase 3)
3. `git revert <test-commit-sha>` (filled in after Phase 4)

The plan-commit and the two bootstrap-exception commits
(`c2aca79` for `/skill-bootstrap`, and the forthcoming `/enrich`
flip) can each be reverted independently. Reverting either
exception commit alone disables half of the recovery chain but
does not break existing usage.

## Plan-14 follow-up — `/demo` is model-invocable (BR-DEMO-006)

After Phase 2 landed, `/demo otel` validation surfaced one more
gap: `/demo`'s frontmatter still carried
`disable-model-invocation: true`, blocking Claude from chaining
`/demo` for its integration-test purpose (BR-DEMO-001 names
this dual contract). One follow-up commit flips the flag to
`false`, adds `BR-DEMO-006` to capture the rule, and extends
`DemoPreflightRecoveryTests.cs` with a single inspection test.
The HITL gate is preserved by `BR-DEMO-005`'s `RECOVERY_AVAILABLE
v1` pattern — the change is about reachability, not removing
the user from the loop.

## Out of scope

- Making `/enrich` accept arbitrary callers without
  validation — the existing `BR-ENRICH-001` / `BR-ENRICH-002`
  rules still apply at the dispatch layer regardless of who
  invokes it.
- Auto-recovering when the **Go OTEL collector** (`:4318`,
  `:13133`, `:13134`) is down — that's `/otel up`'s job and is
  already wired (BR-SKILL-015 surface (c)). This plan is
  about the deterministic-helpers sidecar (`:5050`) only.
- Auto-installing .NET 10 SDK or any other prerequisite
  (`BR-SECURITY-003` forbids it).
- Touching the `:4318` foreign-process detection logic in
  `/otel up` — Plan-14 only consumes the same suppression
  principle for `:5050` in `/demo` and `/extend-skills`.
