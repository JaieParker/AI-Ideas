# Retrospectives

Brief retros after every user-requested change of meaningful
scope, per `BR-PROCESS-002`. Newest entry at the top so the most
recent learning is the first thing a reader sees.

Format: three sections — *What happened*, *What could be
improved*, *Strategies for next time*. Bullets only. No
platitudes. ~200 words total.

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
