# Process incidents

A log of times the project's own process was bypassed, what went
wrong, and what we did about it. New entries land at the top so
the most recent is the first thing a reader sees.

The point of this file is *not* blame — it's making the failure
modes of our process visible so the rules earn their keep. If a
rule keeps being broken, that's data: either the rule needs to be
hardened, or it's in the wrong place.

---

## 2026-05-03 — Rebuild gap broke OTEL continuity; Plan-7 closes it via stage/promote

### What happened

Every change to the .NET sidecar required the same dance:
`Stop-Process` on `:5050`, `dotnet build`, restart. Between stop
and restart, OTEL was off — `/healthz` unreachable, every
skill's `!` exec falling through to `PRECONDITION_FAIL`,
session-scoped enrichments lost. Once `BR-EXTEND-009` made
plan-tagged sessions part of the contract and `BR-DEMO-004`
made per-step OTEL correlation part of demo reports, the
rebuild gap stopped being cosmetic — it became a contract
violation.

The gap surfaced repeatedly during this session. Across ~15
rebuild cycles in Plans 6 / 7 / 8 implementation, OTEL was off
for ~10-30 seconds each time. JSONL has correlated blank
windows; per-plan filtering at those gap points returns less
data than it should.

### Why it happened

1. **Default ASP.NET Core dev workflow assumes single-instance
   restart.** Build writes to `bin/Debug/`, the running DLL
   is locked, build retries 10× then fails. The standard fix is
   "stop the process first" — works fine when continuity isn't
   load-bearing.
2. **The continuity contract was added INCREMENTALLY.** Plan-6
   (`BR-EXTEND-009`) and Plan-8 (`BR-DEMO-004`) tightened the
   contract; the rebuild gap pre-dated both. By the time
   continuity was load-bearing, the operational pattern that
   broke it was entrenched.
3. **The blue/green pattern is well-known but doesn't auto-
   apply.** It's a deployment pattern, not a dev pattern;
   importing it into the dev workflow required deliberate
   design.

### What we did about it

- Plan-7 introduces stage/promote/discard on
  `IStageableLifecycle`. Build into `bin/Staging/` (no DLL
  lock conflict with running blue); spawn green on `:5051`;
  promote via atomic swap with snapshot-rollback per
  `BR-PROCESS-012`.
- The state machine never ends in "no blue, no green" except
  via explicit user `discard + stop`.
- `BR-PROCESS-011` codifies the three-verb lifecycle as a
  general pattern: every tier-managed component opts in via
  its `Staging` slot.
- `bin/Debug.bak/` is the durable rollback artefact (Option B
  in Plan-7's design — favoured over in-memory snapshots
  because survives orchestrator crashes and provides a
  diagnostic for "what was blue before this break?").

### What we'd do differently next time

- **Pair continuity contracts with continuity infrastructure
  in the same plan.** `BR-EXTEND-009`'s plan-tagging assumed
  continuity that the dev workflow didn't deliver. Either
  introduce both together, OR explicitly note the gap in the
  plan as known-debt.
- **Treat green/blue as a dev-loop pattern, not just a
  production deployment pattern.** The same atomic-swap shape
  that protects production traffic protects local-dev
  telemetry. Same code, different driver.

### Lessons captured

- A "fast" dev loop and a "continuous" telemetry loop are
  in tension; the resolution is parallel-instance + atomic
  swap, exactly as production deployments figured out years
  ago.
- The snapshot directory (`bin/Debug.bak/`) is dual-purpose:
  rollback target AND diagnostic artefact. The Plan-7 design
  optimised for both with one decision.

---

## 2026-05-03 — Demo evidence was ephemeral; Plan-8 makes runs durable artefacts

### What happened

`/demo` produced ~50 lines of structured text rendered into the
Claude session and lost when the session closed. Per-step timing
was invisible (the demo runs in ~2 seconds — too fast for a
human to follow). The OTEL records `/demo` caused the collector
to emit went into `output/telemetry.jsonl` correctly but were
uncorrelated with the demo steps that produced them. A
contributor wanting to review a demo run after the fact had to
either re-run it or open the JSONL file separately and try to
correlate timestamps by hand.

### Why it happened

1. **`/demo` was originally console-output-only.** Plan-3 / Plan-
   4 added the structured PASS|FAIL markers (good for parsing)
   but didn't add persistence (the response is text in a
   conversation turn).
2. **The collector's JSONL was implicitly the evidence.** In
   theory, BR-PROCESS-004 ("the project's own telemetry is its
   own evidence") makes JSONL load-bearing. In practice, no
   tooling correlated JSONL records back to the demo step that
   triggered them.
3. **No per-step timestamps on `DemoStepResult`.** The shape
   carried number, label, pass, detail — but not when the step
   ran. Without timestamps, correlation with JSONL was
   structurally impossible.

### What we did about it

- Plan-8 introduced `BR-DEMO-004`: every `/demo <domain>` writes
  a durable markdown report at
  `output/demo-reports/<UTC-ts>-<domain>.md` with per-step
  sections that include the OTEL records emitted during each
  step's window.
- `DemoStepResult` gained `StartedAt` / `EndedAt` (Phase 2a).
- `JsonlSliceReader` (Phase 2b) reads `output/telemetry.jsonl`
  with timestamp + session id + plan tag filtering.
- `MarkdownDemoReportWriter` (Phase 2c) renders the
  `DEMO_REPORT v1` layout.
- `DemoDispatchEndpoint` integrates the writer (Phase 2d) and
  appends `Report saved to: <path>` to the console.
- Plan-8 also added `BR-PROCESS-013` as the meta-rule —
  multi-step lifecycle events produce schema-versioned durable
  reports. Plan-6's `ARCHITECTURE_REVIEW v1` and Plan-7's
  `PROMOTE_REPORT v1` follow the same pattern; the rule lands
  at the third concrete example per `BR-PROCESS-005`'s
  evidence-not-speculation principle.

### What we'd do differently next time

- **Build evidence persistence into the FIRST version of any
  multi-step user-facing operation, not retro-fit it.** Plan-8
  is mostly retro-fit; if `/demo` had been designed with
  per-step timestamps and a report writer from day one,
  Plan-8 would have been a one-phase pass over the existing
  code.
- **Treat schema versioning as table stakes for any structured
  output.** `DEMO_REPORT v1`, `ARCHITECTURE_REVIEW v1`,
  `PROMOTE_REPORT v1` — the v-prefix is a one-token cost that
  buys decades of future flexibility.

### Lessons captured

- "It's in the JSONL" isn't enough — evidence has to be
  *correlated* to be useful. Per-step records embedded in the
  report turn JSONL from a flat stream into a navigable map.
- Naming the pattern (BR-PROCESS-013) is the third-example
  move. The first two (Plan-6's review records, Plan-7's
  promote logs) revealed the shape; Plan-8 codifies it.

---

## 2026-05-03 — No architecture-fit gate; Plan-6 closes the gap

### What happened

Across Plans 1-5, plans were drafted, committed, and implemented
without a structured review step that asked "does this fit the
architecture or extend it?". `BR-PROCESS-005` required flagging
deviations and `BR-PROCESS-006` required ≥3 perspectives, but
both relied on author discipline at plan-drafting time. Several
times during Plan-5 implementation, the user surfaced concerns
post-hoc that an automated review would have flagged sooner: the
`/otel-extend` → `/extend-skills` rename (sat in the codebase
for too long because nothing prompted "is this name still
honest?"), the IDomain → IDomainDemo split (initially
out-of-scope; the user had to manually surface "remember domains
need a demo"), the demo-as-skill-orchestrator correction.

### Why it happened

1. **No structured review checkpoint.** The flow had Phase 0
   (pre-flight) → Phase 1 (plan) → Phase 2 (implement) etc.,
   but no point at which someone (human or automated) was
   forced to read the drafted plan against the project's
   architectural commitments and emit a structured opinion.
2. **Author-discipline-only enforcement.** `BR-PROCESS-005` /
   `BR-PROCESS-006` describe what to do but don't enforce
   doing it. A reviewer might or might not surface a concern;
   when they did, it was post-hoc.
3. **No durable record of architecture decisions.** When a
   concern WAS raised and resolved, the resolution went into
   commit messages or chat — not into the plan file as a
   structured artefact. Future contributors couldn't tell
   "did we evaluate this question?" from the plan file alone.

### What we did about it

- Plan-6 introduces `/architecture-review` as a Shape-B skill:
  the dispatch endpoint loads context (CLAUDE.md, business-
  rules, recent plans, target body, the resolved domain's
  TrustedReferences) and renders a structured prompt; Claude
  is the analyst per `BR-SKILL-012`. Output follows the
  `ARCHITECTURE_REVIEW v1` schema.
- `BR-PROCESS-009` adds the human-decision gate. EXTENDS rows
  resolve to one of Evolve / Constrain / Defer / Override and
  land in the plan file's `## Architecture review decisions`
  section. Plan-2 of `/extend-skills` won't proceed without
  every commitment having a recorded resolution.
- `/helpers/plans/architecture-review-gate` is the deterministic
  helper that verifies the section is populated correctly. Per
  `BR-SKILL-006` the deterministic part lives in the sidecar;
  per `BR-SKILL-012` the review itself stays qualitative.
- `BR-EXTEND-009` adds plan-tagged sessions. Every flow run
  enriches the session with `plan:<filename>`; per-plan filter
  of OTEL records is one grep.

### What we'd do differently next time

- **Land the review-and-gate machinery before the third plan
  needs it.** Plan-6 lands two plans late; if the gate had been
  in place when Plan-5 started, the IDomain→IDomainDemo split
  and the rename's domain-arg shape would have surfaced via
  EXTENDS rows during Phase 1.5 rather than via post-hoc user
  redirects.
- **Treat process-rule additions as needing process-rule
  enforcement.** `BR-PROCESS-005` and `BR-PROCESS-006` were
  added without enforcement; they relied on discipline. The
  gate-check (a deterministic helper) is the missing
  enforcement layer.

### Lessons captured

- A rule that depends on author discipline alone is a soft rule.
  Hard rules need a deterministic check OR a structured prompt
  that makes the question visible. Plan-6's combination
  (qualitative prompt + deterministic gate) is the project's
  template for future "must-be-evaluated" rules.
- Per `BR-PROCESS-005`'s evidence-not-speculation principle,
  the rule lands at the third concrete example. Plans 6, 7, 8
  each produce a lifecycle-event report; `BR-PROCESS-013`
  (Plan-8) names the pattern. Plan-6's gate is similar — it
  formalises a check we'd been doing ad-hoc.

---

## 2026-05-03 — OTEL hardcoded for too long; rename + IDomain interface introduced

### What happened

`/otel-extend` was named after the OTEL domain even though its
behaviour is generic — it drafts plans, applies changes, rebuilds,
tests, commits per phase. Same for `/demo` (the dispatch had 14
hardcoded OTEL-flavoured live steps) and `NextPlanFileName`
(hardcoded `"The-OTEL-Plan-..."` regex). The hardcoding was
invisible until a second domain (kai-platform, prototyping at
`/c/Work/kai-platform`) needed to land. Without the rename and
interface, integrating it would have forced either a rename
later under pressure or a parallel fork of every flow.

### Why it happened

1. **Single-domain inertia.** The project shipped with one
   domain. Generalising before the second example existed felt
   like premature abstraction (`BR-PROCESS-005` evidence rule).
   That instinct is correct *for the abstraction's shape* but
   wrong *for the boundary's location* — the latter can be drawn
   confidently from one example.
2. **Naming-as-scope confusion.** A skill named `/otel-extend`
   reads as OTEL-specific even though its behaviour is generic.
   The name shapes the contributor's mental model; over time,
   OTEL-specific quirks accrete "because the name says so".
3. **No prompt to surface the latent decoupling.** The tier-
   philosophy (`BR-PROCESS-008`) exists for runtime tiers
   (sidecar / collector); a parallel principle for *naming
   tiers* (generic vs tenant-specific) was missing.

### What we did about it

- Plan-5 introduces `IDomain` as a decentralised contract — each
  domain self-implements; no central registry; no consumer
  changes when a new domain registers.
- Renamed `/otel-extend` → `/extend-skills` (Phase 2c) with
  `<domain>` as the first arg.
- Extracted `/demo`'s 14 OTEL live steps into `OtelDomainDemo`
  implementing the `IDomainDemo` companion contract (Phase 2d).
- Added `/domain-info` (Phase 2e) for read-only knowledge
  queries over any subset of an `IDomain`'s slices.
- Added `BR-EXTEND-006` (the contract), `BR-EXTEND-007` (domain-
  neutral skill names), `BR-EXTEND-008` (curated trusted
  references), `BR-EXTEND-010` (domains expose demos via
  `IDomainDemo`).
- The kai-platform integration becomes mechanical: one new
  `KaiPlatformDomain : IDomain` class + one DI registration in
  `Program.cs`. Optional companion `KaiPlatformDomainDemo :
  IDomainDemo`. Zero changes to existing consumers.

### What we'd do differently next time

- **Generalise the boundary before the second example, not the
  shape.** "Where does the abstraction live?" is answerable from
  one example; "what's the abstraction's full shape?" is not.
  The interface route (vs centralised registry) is the
  expression of that distinction — it commits to *where* without
  committing to *how-much*.
- **Surface naming-as-scope in the rule register.** `BR-EXTEND-007`
  ("domain-neutral names when generic") is the rule that would
  have prompted the rename earlier. Going forward it applies on
  every new skill.
- **Treat "the second example incubates elsewhere" as a real
  signal.** kai-platform was prototyping in
  `/c/Work/kai-platform`; that's enough evidence to design the
  interface against — even though the second domain hasn't yet
  landed in this repo.

### Lessons captured

- A name is a contract with future contributors. `/otel-extend`
  promised OTEL-specificity its behaviour didn't deliver;
  `/extend-skills` promises genericity that the implementation
  now keeps.
- The pivot didn't introduce new functionality — it removed a
  coupling. Six commits across Plan-5 phases 2a–2e changed the
  shape of how domains plug in without changing what any
  existing skill *does*. That's the test of a clean refactor:
  zero behavioural diff for the existing domain, large optionality
  gain for future ones.

---

## 2026-05-03 — `/demo` failed silently because every skill assumed the sidecar was up

### What happened

The user ran `/demo` to validate the project end-to-end. The
skill's `!` preprocessing line is `curl http://127.0.0.1:5050/skills/demo/dispatch ...`
with no fallback. The sidecar was not running. `curl` exited
with code 7 (connect refused), the `!` exec aborted, and the
skill body never reached Claude. The user saw raw shell
stderr (`curl: (7) Failed to connect`) instead of any usable
diagnostic from the skill.

The same failure mode applies to every other dispatching skill
in the project (`/otel`, `/enrich`, `/weather`, `/otel-extend`).
None of them have a probe-or-instruct fallback. The cost of any
skill being run with the sidecar down is identical: opaque
shell error, no actionable next step.

The project also had no skill that could *bring up* the sidecar.
`/otel` reports its status but routes through the very sidecar
that may be down. Chicken-and-egg with no escape.

### Why it happened

1. **No precondition guarantee in the skill contract.** Skills
   were designed as "markdown + one curl line"; the assumption
   that the sidecar is "always up" was never gated by a probe.
2. **No bootstrap skill at the platform tier.** `/otel` is an
   OTEL-tenant skill that happens to live behind the sidecar;
   it cannot bootstrap the platform it depends on.
3. **`BR-PROCESS-001` permitted only one bootstrap exception**
   (the commit that built `/otel-extend`). A second
   bootstrap-class skill (`/skill-bootstrap`) was needed but the
   rule's text didn't anticipate the platform/tenant
   distinction.

### What we did about it

- Added a second named bootstrap exception under
  `BR-PROCESS-001`: the commit that builds `/skill-bootstrap`.
  Both exceptions share the same shape ("the committed skill is
  the bootstrap mechanism for some downstream rule").
- Built `/skill-bootstrap` end-to-end as that named commit:
  pre-requirement table (5 rows: .NET SDK, source present, built
  artefacts, port 5050 free or owned, healthz reachable), verbs
  for `install` (dotnet build), `start` (spawn + poll), `stop`
  (port-listener kill).
- Ran `/skill-bootstrap install` and `/skill-bootstrap start` to
  bring the platform up. From that point onward, every other
  phase of the fix landed through `/otel-extend` properly.
- Followed up with `BR-SKILL-010`: every dispatching skill's `!`
  exec line MUST end with `|| printf 'PRECONDITION_FAIL: ...
  /skill-bootstrap ...'` so the fallback always reaches Claude.
  A lint test enforces the convention; `/skill-bootstrap` is the
  single named exemption.
- Restructured `/demo` to demonstrate the new bootstrap story:
  it shows the off-state honestly (everything FAIL), names the
  exact commands to bring it up, then walks the configure /
  enrich / re-run / teardown flow once the platform is on.

### What we'd do differently next time

- **Build the platform-tier bootstrap before any tenant-tier
  skills.** The order should be: deterministic-helpers sidecar
  → `/skill-bootstrap` (platform) → `/otel-extend` (governance)
  → tenant skills (`/otel`, `/enrich`, `/weather`, `/demo`).
  This project shipped them in the wrong order and the failure
  surfaced exactly when a new user (the user) tried to onboard.
- **Treat skill-onboarding as part of the skill contract.** Any
  new skill must answer "what does this look like the first time
  someone runs it on a clean machine?" before it ships.
- **Lint-as-precondition.** A documentation rule ("every skill
  has a fallback") is necessary but not sufficient.
  `BR-SKILL-010`'s lint test makes the rule unbypassable.

### Lessons captured

- "Markdown skills + one curl" is an elegant pattern, but elegance
  hides preconditions. The pattern needs a probe-and-fallback
  structure baked in to be safe.
- Bootstrap-class skills are a real category. Two now exist
  (`/otel-extend`, `/skill-bootstrap`); future ones will be rare
  but possible. The rule register names them explicitly.

---

## 2026-05-03 — Go collector chosen reflexively (round 2); pivoted to .NET-only

### What happened

The user pointed at `open-telemetry/opentelemetry-dotnet` and
asked "what is this?". My earlier framing — "OCB only produces
Go binaries; the upstream OTel Collector is a Go project" —
was technically correct about the *collector framework* but had
been used to lock in "OTel = Go". The .NET ecosystem has full
first-class OTel SDK support, and our actual collector
responsibilities (OTLP/HTTP receiver, three small processors, a
JSONL exporter) do not require the Go collector framework at
all.

This is the *second* time the same question was raised against
the same choice. The first round (`docs/process-incidents.md`,
2026-05-02 entry "Go-via-OCB chosen silently; post-hoc
validated") settled on "defensible because of the contrib
ecosystem". That post-hoc rationalisation was weaker than it
looked, because we don't actually use any contrib component
beyond `otlpreceiver` and `fileexporter` — both small enough
to re-implement in .NET.

### Why it happened

1. **`BR-PROCESS-005` was applied at the wrong scope.** The
   rule fires on architectural decisions, but I had not
   inventoried the *components* of the Go collector we
   depended on. A component-level inventory would have shown
   the dependency was thin.
2. **"Defensible" became a stop word.** Once the Go choice
   was post-hoc validated, the question stopped being asked.
   "Defensible" is not the same as "best".
3. **The chain-out option was documented as theoretical.**
   The 2026-05-02 entry mentioned forwarding enriched OTLP to
   a downstream stock contrib binary as the contrib-access
   answer. That option means the contrib ecosystem is
   available at *runtime* without Go being a *build*
   dependency.

### What we did about it

- Pivoted the collector to a new .NET project (`src/Collector/`):
  OTLP/HTTP receiver on `:4318` (using the `OpenTelemetry.Proto`
  protobuf NuGet), per-session enrichment processor, collection-
  toggle filter, persistent-enrichments processor, JSONL
  exporter, control API on `:13133`, healthz on `:13134`.
- Ported the Go-side `BR-ENRICH-*` and `BR-OTEL-*` tests to .NET.
  The HTTP-contract shape transferred directly.
- Moved the Go collector source to `tools/legacy/go-collector/`
  for reference; removed the OCB build step from the project's
  primary build path.
- Removed Go from `BR-SKILL-008`'s accepted-dependency list. The
  project is now .NET-only at the build level.
- Documented "chain-out to OCB" as a runtime composition option:
  if a future use-case needs a contrib exporter, the .NET
  collector forwards OTLP to a sibling OCB binary. No build
  dependency added.
- Extended `/skill-bootstrap` to cover the collector tier (build,
  start, stop), since it is now a .NET project. `/otel up` and
  `/otel down` were considered but not added — the platform-tier
  bootstrap covers both tiers because both are .NET.

### What we'd do differently next time

- **Inventory components, not frameworks.** When a framework
  choice is being justified by ecosystem access, list the
  specific components consumed. If the list is short, the
  ecosystem-access argument is weak.
- **Make "defensible" a downgrade signal.** If a choice is
  merely "defensible", the question hasn't been asked hard
  enough. Re-open it explicitly.
- **Treat runtime composition as a first-class option.** Build-
  time language coupling is heavier than runtime HTTP coupling.
  The chain-out pattern (one service forwards to another over
  the wire) preserves ecosystem access without language burden.

### Lessons captured

- The same question came up twice for the same decision in two
  consecutive days. That is the signal that the decision was
  not actually settled the first time. Post-hoc validation
  paints over rather than answers.
- A user with a question link is a free architectural review.
  Treat it as such.

---

## 2026-05-02 — Architecture trade-off enumeration was one-sided

### What happened

When recommending the .NET-only collector pivot, I listed three
"losses" from the engineering perspective and called the
analysis done. The user pushed back: "Access to the contrib
processor ecosystem - That is not an acceptable loss - what
losses were not considered?".

Ten material losses were missing from the analysis:

1. Compatibility with future OTel community innovation.
2. Operator familiarity (.NET vs OTel knowledge for handoff).
3. Vendor support patterns (Honeycomb, Datadog etc. all
   prescribe contrib).
4. OTel-related tooling alignment (telemetrygen, debug
   exporter, CLIs).
5. The OCB workflow itself (manifest-driven, version-pinned,
   blessed).
6. OTel spec evolution (OTLP v2, profiles signal).
7. Conformance testing.
8. Documentation alignment with the broader ecosystem.
9. Component interop (well-tested ordering, batching,
   attribute resolution between stock components).
10. Reputational/strategic position of "we run the standard".

All ten live in the **operations**, **strategy**, and **user-
facing** lenses — perspectives I never explicitly took. My
analysis was three sub-views of the engineering lens, which is
exactly the failure mode `BR-PROCESS-006` now exists to prevent.

### Why it happened

1. **One-perspective bias.** I framed the pivot from "code we
   write" alone. The losses I enumerated (re-implementing
   batching, retry, rotation) are all engineering. I never
   explicitly asked "what does this look like from operations?"
   or "what does this look like from strategy?".
2. **Asymmetric visibility.** Gains tended to be visible from
   the engineering frame I was already in (one language, less
   code, no Go upgrade). Losses lived in adjacent frames I
   hadn't taken (operator familiarity, vendor patterns,
   ecosystem evolution).
3. **No process gate forced the perspective rotation.** Until
   `BR-PROCESS-006`, the rules didn't require multi-perspective
   analysis. With it, the perspective rotation becomes a
   checklist item, not a remembered habit.

### What we did about it

- Surfaced all ten missed losses immediately in the response
  that followed the user's challenge.
- Added `BR-PROCESS-006`: every architectural change analysis
  must enumerate pros/cons from at least three orthogonal
  perspectives. CLAUDE.md grows a dedicated section listing the
  standard lens set (Engineering / Operations / Strategy /
  User-facing / Security / Cost).
- Re-applied the rule to the same pivot question: the
  "chain-out" architecture (our .NET service forwards enriched
  OTLP to a downstream stock contrib binary) preserves contrib
  ecosystem access while keeping authored code .NET-only. Three
  orthogonal perspectives — engineering, operations, strategy —
  all came out positive.

### What we'd do differently next time

- **Apply BR-PROCESS-006 the moment a pivot is being
  recommended**, not after challenge. The cost of three
  perspectives is trivial; the cost of skipping them is a
  recommendation that goes nowhere.
- **Start with the perspective that contradicts the
  recommendation** as a forcing function. If the recommendation
  is "fewer languages", start the analysis from "what does this
  cost the operator who has to debug it?" — that's the lens the
  recommender is least likely to be in.

### Lessons captured

- A "pros and cons" section that's all from one perspective is
  a red flag. The shape of the answer reveals the bias of the
  questioner.
- Ten losses in one challenge is a lot. The asymmetry between
  what I surfaced (3 minor losses) and what was actually there
  (10+ material ones) shows the magnitude of the gap.

---

## 2026-05-02 — Go-via-OCB chosen silently; post-hoc validated

### What happened

Early in the architecture conversation I locked in "the OTel
Collector is built in Go via OCB". The rationale was sound — OCB
is Go-only, the upstream Collector framework is Go, the
component ecosystem is Go-native — but it landed in the plan
without ever being flagged as a choice. No alternatives were
enumerated; in particular, no consideration was given to whether
a .NET service that re-implements just the slice we need could
have done the same job. (It could have.)

### Why it happened

1. **Pattern-matching to "obvious" answers.** OCB → Go → end of
   thought. I treated the question "what language for the
   collector" as having one answer because the framework I'd
   identified was Go-only. The deeper question — "do we need to
   use that framework at all?" — wasn't asked.
2. **No process gate forced the question.** There was nothing
   in the rule register at the time that said "before you commit
   to a language, enumerate alternatives." Without a gate, the
   default is to proceed.

### What we did about it

- The user surfaced the gap by asking "did you check?".
- Validated the choice post-hoc: yes, .NET could in principle
  build a custom OTLP receive/process/export service, but the
  Go ecosystem and the work already done in .NET on the helpers
  side make the status-quo the right answer.
- Added `BR-PROCESS-005` so this category of silent lock-in is
  required to be flagged in future. CLAUDE.md grows a "Flag
  significant architectural decisions" section.
- The deviation rationale (why Go for the collector specifically
  rather than .NET-only) is now documented in three places:
  `BR-SKILL-008`'s exception list, the deciding commit's message,
  and this incident log.

### What we'd do differently next time

- **Apply BR-PROCESS-005 the moment the rule lands.** The very
  next architectural choice gets flagged, even if it feels
  obvious. The friction is the point.
- **Build a small "alternatives I considered" habit.** Before
  recommending a tech, write down at least one alternative —
  even if I dismiss it in a sentence. The habit is the
  prophylactic.

### Lessons captured

- "Obvious" is a strong signal that the question wasn't asked
  hard enough. The Go-via-OCB choice felt obvious because the
  search space had already been narrowed; widening it would
  have surfaced the .NET option in seconds.
- Post-hoc validation is cheaper than a pivot but more expensive
  than a flag. Flags should happen at decision time, not at
  audit time.

---

## 2026-05-02 — Skill changes hand-rolled instead of via `/otel-extend`

### What happened

Across this session, several commits modified files under
`.claude/skills/**` and `src/HelpersSidecar/Endpoints/*Dispatch*`
directly, with hand-rolled git commits, instead of going through
the `/otel-extend` self-modification flow that the design
explicitly created for that purpose.

Affected commits:

- `a5bd276` feat(skills): /weather example skill
- `6ac5c9b` feat(skills): /enrich skill + Node helper
- `a932600` refactor(skills): dispatch moves into the sidecar; Node helpers gone
- `11e8e2e` chore(skills): tighten allowed-tools to URL-prefix patterns

### Why it happened

1. **`/otel-extend` was designed but never built.** The
   `The-OTEL-Plan.md` describes the multi-phase flow (plan →
   implement → build → test, each gated by user confirmation,
   each phase committed separately). Its `SKILL.md`, supporting
   files (`playbook.md`, `phases.md`), and the
   `/skills/otel-extend/dispatch` sidecar endpoint were never
   implemented. There was no flow to route changes through.

2. **Implementation pressure made bypass cheap.** Each turn
   produced a small focused change. Pausing to build
   `/otel-extend` (itself a substantial multi-file commit) would
   have stalled the iteration. The path of least resistance was
   to hand-roll the commit and move on.

3. **The cost of bypass was less visible than the cost of using
   the flow.** Hand-rolled commits gave clean git history, clear
   messages, and individual reverts — they *looked* fine. The
   cost of *not* using `/otel-extend` was missing plan
   documents, missing per-phase gates, missing test-phase
   confirmations — losses you only notice when someone asks
   "where did this rule originate?" or "why was this change
   sequenced this way?".

4. **No deterministic enforcement existed.** CLAUDE.md guidance
   is read by Claude every session, but it's a soft rule:
   readable, persuasive, easy to rationalise around. There is
   currently no Claude Code hook that *blocks* writes to
   `.claude/skills/**` outside the flow.

### What we did about it

- Added **`BR-PROCESS-001`** — skill changes go through
  `/otel-extend`. Captured in
  `docs/business-rules.md`.
- Added a CLAUDE.md section spelling the rule out so it's loaded
  into every session and visible to Claude every turn.
- Acknowledged that one **bootstrap exception** is allowed: the
  hand-rolled commit that *builds* `/otel-extend` itself. The
  flow can't govern its own creation.
- Logged this incident here so the failure mode is explicit and
  the next contributor doesn't repeat it.

### What we'd do differently next time

- **Build `/otel-extend` before the first skill commit.** In any
  future "introduce skills" implementation cycle, the order is:
  bootstrap `/otel-extend` first, *then* every other skill
  through it. The bootstrap exception is the only acceptable
  hand-roll.
- **Harden enforcement with a hook.** A Claude Code `PreToolUse`
  hook on `Edit` / `Write` that blocks writes to paths matching
  `.claude/skills/**` unless an environment variable set by
  `/otel-extend` is active would make this bypass impossible
  rather than merely discouraged. Tracked as a v2 hardening item.

### Lessons captured

- Rules in documentation are necessary but not sufficient. They
  rely on the discipline of whoever reads them. For things you
  genuinely want to enforce, a hook (or equivalent technical
  gate) does what prose cannot.
- "We'll build the gate later" is the easiest place for the gate
  to never get built. If it matters, build it first.
- The chicken-and-egg problem of a self-modification flow having
  to govern its own modifications has exactly one solution:
  acknowledge it, build the flow under one named exception, then
  let the rule apply universally going forward.
