# Process incidents

A log of times the project's own process was bypassed, what went
wrong, and what we did about it. New entries land at the top so
the most recent is the first thing a reader sees.

The point of this file is *not* blame — it's making the failure
modes of our process visible so the rules earn their keep. If a
rule keeps being broken, that's data: either the rule needs to be
hardened, or it's in the wrong place.

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
