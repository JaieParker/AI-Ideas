# Process incidents

A log of times the project's own process was bypassed, what went
wrong, and what we did about it. New entries land at the top so
the most recent is the first thing a reader sees.

The point of this file is *not* blame — it's making the failure
modes of our process visible so the rules earn their keep. If a
rule keeps being broken, that's data: either the rule needs to be
hardened, or it's in the wrong place.

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
