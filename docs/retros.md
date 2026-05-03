# Retrospectives

Brief retros after every user-requested change of meaningful
scope, per `BR-PROCESS-002`. Newest entry at the top so the most
recent learning is the first thing a reader sees.

Format: three sections — *What happened*, *What could be
improved*, *Strategies for next time*. Bullets only. No
platitudes. ~200 words total.

---

## 2026-05-03 — Action-plan execution: 7 items closed in one session

**What happened**

- The Plan-9 retro produced a 7-item action plan. User explicitly
  rejected "skip" as a category and asked for everything to land
  in dependency order with parallelism where possible.
- Captured the meta-rule as `BR-PROCESS-014` first, then executed
  the plan: `tools/show-records.py` committed, `CrossDomain`
  virtual domain added, `/helpers/plans/index` endpoint built
  with live `docs/INDEX.md` written against the registry,
  `/helpers/integration-test-scope` endpoint wired BR-EXTEND-011
  to a callable HTTP surface.
- Verified end-to-end: `/extend-skills otel <topic>` correctly
  scans `docs/otel/plans/` and proposes Plan-10 as the next
  filename; `/demo otel` completes 14/14 PASS against the new
  layout; `/helpers/integration-test-scope` returns
  `crossDomainTriggered=true` for top-level shared files and
  `false` for per-domain changes.
- 5 commits, 18 new tests (298 → 316), no regressions.

**What could be improved**

- I sequenced the work but didn't surface the dependency graph
  to the user **before** starting — exactly the strategy I
  captured in the previous retro and just made into
  `BR-PROCESS-014`. The user's "proceed" was implicit consent,
  but the rule asks for explicit graph-then-action. I executed
  by habit instead of by the new rule on its first day.
- The PlansIndexBuilder has a small DI miss: it's instantiated
  inline by the endpoint handler rather than registered as a
  singleton. Works fine but inconsistent with the project's
  pattern of putting application services on the container.
  Worth tidying when this code is next touched.

**Strategies for next time**

- **Surface the dependency graph before starting any multi-item
  action plan**, even when the user has already said "proceed".
  Concrete (a numbered list with arrows for ordering and "[]"
  brackets for parallelisable groups), testable (a reviewer can
  ask "did the actor render the graph before the first
  commit?"), applies any time the action list has ≥ 3 items.
  - `stage[concrete-and-testable] 1/1`
  - `stage[applied-in-real-change] 0/3`
- **Register application services on the DI container by
  default**, not inline in endpoint handlers. The project's
  existing pattern is `AddSingleton<TInterface, TImpl>()`; new
  Application/* classes follow the same shape. Concrete (search
  for `new Foo(` inside endpoint handlers; each instance is a
  candidate), testable (a static check could enforce it later),
  applies in code reviews of new endpoints.
  - `stage[concrete-and-testable] 1/1`
  - `stage[applied-in-real-change] 0/3`

---

## 2026-05-03 — Plan-9 retro + 7-item action plan + BR-PROCESS-014 capture

**What happened**

- Plan-9 (per-domain plan files) shipped end-to-end through the
  full `/extend-skills` flow including Phase 1.5 architecture
  review. 8 commits, 273 → 298 tests, no regressions.
- Two user constraints landed mid-flow ("don't lose cross-domain
  discoverability"; "this also tells us domain-scoped integration
  testing"). Both absorbed into the same flow as new BRs with
  passing tests (`BR-EXTEND-011`, `BR-CODE-004`).
- The Plan-9 retro produced a 7-item action plan. User explicitly
  rejected "skip" as a category — every item must be actioned or
  recorded as deferred. This became `BR-PROCESS-014`.

**What could be improved**

- I added the BR-EXTEND-011 EXTENDS marker to Plan-9 manually
  after the live `/architecture-review`, when ideally I would
  have re-run the review against the updated plan. The gate
  passed with three resolutions even though the plan now had
  four EXTENDS-equivalent points.
- I offered to `/schedule` a 2-week verification of Plan-9
  drift. User correctly pointed out there's no soak window — a
  one-shot refactor is verifiable now, not in 2 weeks. Defer
  offers should match real signals, not pattern-match retros to
  scheduled agents.

**Strategies for next time**

- **Re-run `/architecture-review` when a constraint lands
  mid-flow** rather than hand-editing markers into the plan.
  Concrete (call the dispatch endpoint), testable (the gate's
  marker count must equal the analyst's stated EXTENDS count),
  applicable any time a Phase 2 commit reveals an unrecorded
  EXTENDS. Stage 1 (`concrete-and-testable`) cleared by this
  retro entry.
  - `stage[concrete-and-testable] 1/1`
  - `stage[applied-in-real-change] 0/3`
  - Promotes to a BR after 3 real applications without rework.
- **Match `/schedule` offers to real future signals**, not to
  every retro. Skip the offer for one-shot refactors, bug fixes,
  and anything verifiable now. Concrete, testable (a reviewer
  reads the offer and asks "what's the future signal here?"),
  applies whenever I'm tempted to end a reply with a schedule
  pitch.
  - `stage[concrete-and-testable] 1/1`
  - `stage[applied-in-real-change] 1/3` (applied in this turn —
    declined the 2-week offer when challenged)
- **Surface the dependency graph and parallelisation choice
  before starting an action plan** rather than after. The user
  asked "in dependency order, parallel where possible" — the
  default I gave was sequential. Captured as `BR-PROCESS-014`.

---

## 2026-05-02 — Add BR-PROCESS-006 (≥ 3 orthogonal perspectives) + missed-losses incident

**What happened**

- I recommended the .NET-only collector pivot with a "losses" list
  that turned out to be three sub-views of the engineering lens.
- User vetoed: contrib ecosystem access is not negotiable, AND
  asked what losses I missed. Surfaced 10 more — all in the
  operations / strategy / user-facing perspectives I never
  explicitly took.
- Added BR-PROCESS-006: every architectural change analysis
  must enumerate pros/cons from at least three orthogonal
  perspectives. Standard lens set documented (engineering,
  operations, strategy, user-facing, security, cost). Re-applied
  the rule to the chain-out architecture; all three perspectives
  net out positive within the user's constraints.
- Process incident logged in detail.

**What could be improved**

- Same root failure mode as the URL-404, version-assertion, and
  prebuilt-binary incidents: surfacing one slice of the picture
  and treating it as the whole picture. The pattern shows up in
  research (search vs read), pre-conditions (assert vs verify),
  and now trade-off enumeration (one lens vs three).
- Common fix: a forcing-function rule that converts implicit
  habit into explicit checklist. BR-PROCESS-006 is exactly that
  for trade-off analysis.

**Strategies for next time** *(tracked in docs/retros.md)*

- **NEW: "Start an architectural-change analysis from the
  perspective that contradicts the recommendation."** If
  recommending fewer languages, start from "what does the
  operator who has to debug it lose?". Concrete and testable.
  Marking occasion 1/3.

- **CARRIED, +1: "fetch authoritative file when asserting (a
  pre-condition / a capability / a loss enumeration)."** The
  surface area of this strategy keeps widening — same shape, new
  domain. Counter holds at 3/3 on stage 2; one more
  no-rework occasion advances stage 3 to 1/3.

- **NEW: "When a recommendation gets vetoed, list the missed
  losses BEFORE proposing a replacement."** Otherwise the
  replacement looks like deflection. Concrete and testable.
  Marking occasion 1/3.

---

## 2026-05-02 — Add BR-PROCESS-005 (flag architectural decisions; document deviations)

**What happened**

- User asked "did you check whether a .NET equivalent of the
  OTel Collector exists?" — I hadn't. The Go-via-OCB choice was
  silently locked in earlier in the session.
- Validated post-hoc: a .NET path is technically viable but the
  Go ecosystem + work already done on the helpers in .NET makes
  the status quo right. Decision held.
- Captured the procedural failure as `BR-PROCESS-005` (flag
  architectural choices, document deviations) and as a process
  incident. The CLAUDE.md section spells out the flag procedure
  and the deviation-documentation requirement.

**What could be improved**

- I treated "OCB → Go" as a closed question because the
  framework I'd identified was Go-only. The deeper question —
  "do we need to use that framework at all?" — wasn't asked.
  Lesson: when a decision feels obvious, that's the moment to
  widen the search space, not narrow it.
- Three architectural choices in this session have followed the
  same pattern (Go collector, Node helpers, helpers sidecar in
  .NET). Each deserved a flag; only the first failure surfaced.

**Strategies for next time** *(tracked in retros.md)*

- **NEW** "Before recommending a language/runtime/framework,
  explicitly write down at least one alternative — even if you
  dismiss it in a sentence." Concrete, testable. Marking
  occasion 1 of 3.

  - Strategy: "explicitly enumerate at least one alternative
    when recommending a language/runtime/framework"
    evidence: (default schema)
    stage[applied-in-real-change] 1/3 in commit (this commit)

- **CARRIED** "Default to arrays not dicts when modelling
  ordered progression" — counter at 2/3 from prior retros.

- **CARRIED** "Enumerate deterministic data sources before
  defaulting to HITL when designing validation" — counter at
  1/3 from prior retros.

---

## 2026-05-02 — Add BR-PROCESS-004 (evidence sources can be deterministic or HITL)

**What happened**

- Initial proposal said evidence is collected via retros (HITL).
  User pushed back: deterministic gates shouldn't need a human.
- Iterated to a `source` field per gate with four supported
  values: `hitl-retro` (default), `otel-query`, `ci-signal`,
  `command-probe`.
- The kicker: the project's own OTEL output is the canonical
  evidence source for skill-related gates. Skills already emit
  `claude_code.skill_activated` events; adding a marker
  attribute and querying the local JSONL yields a deterministic
  count. The system observes its own behaviour.
- Landed in CLAUDE.md and `docs/business-rules.md` (BR-PROCESS-004).
  No source code changes — purely process/schema.

**What could be improved**

- I proposed only HITL as the data source in the previous turn.
  Defaulting to "human reviews everything" is a familiar trap
  for tooling — it works, but it's expensive when half the
  questions are answerable from data the system already
  produces. Lesson: when designing a counting/validation
  scheme, ask "which of these gates has structured data
  already?" before defaulting to HITL.

**Strategies for next time**

- "When designing a counting / validation mechanism, enumerate
  data sources before defaulting to HITL." Concrete enough to
  test (a retro reviewer can ask "did we list candidate
  sources?"). Marking as occasion 1 of evidence collection.

  - Strategy: "enumerate deterministic data sources before
    defaulting to HITL when designing validation"
    evidence: (default schema)
    stage[applied-in-real-change] 1/3 in commit (this commit)

- "Default to arrays, not dicts, when modelling ordered
  progression" (carried forward from previous retro).

  - Strategy: "default to arrays not dicts when modelling
    ordered progression"
    evidence: (default schema)
    stage[applied-in-real-change] 2/3 in commit (this commit) —
    second occurrence: this BR's `stages` is also an array.

---

## 2026-05-02 — Add BR-PROCESS-003 (evidence-driven promotion / demotion)

**What happened**

- Iterated the "3 strikes promotes" idea into a full schema:
  `evidence.stages` is an ordered array of `{ gate, min }` pairs;
  settable per-skill (SKILL.md frontmatter) or per-strategy
  (inline in retros.md). Counts tracked visibly next to each
  strategy. Demotion mirrors promotion via the same machinery.
- Two clarifications from the user during the design:
  1. Stages are an **array** (ordered), not a flat dict.
  2. The whole block must be **settable** (overridable) at skill
     and strategy level.
- Landed in CLAUDE.md (full procedure) and
  docs/business-rules.md (`BR-PROCESS-003`).

**What could be improved**

- The first draft used a flat `stages: { proposed: ..., applied:
  ..., validated: ... }` map. The user corrected to an array
  with explicit `min` counts per gate. Lesson: when designing a
  schema with progression semantics, default to an array; dict
  ordering is implicit and fragile.
- This is now a *third* process rule landed without the
  evidence machinery applied to itself. The retro entries for
  `BR-PROCESS-001/002/003` should themselves be the first
  populated counters once the machinery is bootstrapped — but
  there's no tooling yet to track them.

**Strategies for next time**

- Any new schema with progression semantics: model as an array
  of `{ key, constraint }` objects from the start, even if it
  feels like overkill at draft time. Cheaper than a v2 reshape.
- The first concrete promotable strategy: "default to arrays,
  not dicts, when modelling ordered stages." Mark it in this
  retro for evidence collection — this is occasion 1/3 of
  `applied-in-real-change` per BR-PROCESS-003's default schema.

  - Strategy: "default to arrays not dicts when modelling ordered
    progression"
    evidence: (default schema)
    stage[applied-in-real-change] 1/3 in commit (this commit)

---

## 2026-05-02 — Add BR-PROCESS-002 (retro-after-every-change)

**What happened**

- User asked for a retro after every requested change.
- Added `BR-PROCESS-002` to the rule register, a CLAUDE.md
  section spelling out the format and length cap, and this
  retros log file with its first entry (this one — meta but
  honest).
- All in one commit; no source code touched.

**What could be improved**

- The retro rule is going into a session where five other
  process-level rules (`BR-SKILL-007/008/009`,
  `BR-PROCESS-001`, `BR-CODE-001`) have already been added in
  the same conversation. Each landed in its own commit, but they
  weren't surfaced as "we are now hardening process discipline"
  — they read as a string of small additions. A summary commit
  ("project process discipline pass") at the end would have
  made the intent visible.
- The retro itself only ever triggers if I remember the rule
  exists. CLAUDE.md is loaded into every session, so this is
  reasonably reliable — but the same critique that applies to
  `BR-PROCESS-001` (soft enforcement only) applies here.

**Strategies for next time**

- When two or more process rules land in the same session, end
  the session with a "process pass: rules added X/Y/Z" summary
  pointer in `docs/process-incidents.md` or this file.
- Pair the retro rule with the CI-level lint that already
  parses test names for BR IDs; extend it to flag commits whose
  message contains `feat:` or `fix:` but the response that
  produced them had no retro section. Soft signal, no hook.
- Keep retros short. The discipline is "make it concrete and
  useful", not "fill three paragraphs". If a section is empty,
  write one sentence that says so honestly.
