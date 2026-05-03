# Skill Discernment cleanup — bring every local skill to 8/8

> Plan-12 follows Plan-11 (artefact registry) which surfaced
> the next concrete cleanup target via `/ai-level local`'s
> 60/72 score: 7 of 9 skills score below 8/8 — every single
> one missing the **Discernment** dimension's "schema-version
> marker in body" check, several missing BR citations, and
> `weather` additionally missing explicit `disable-model-invocation`
> in frontmatter.
>
> This is a focused sweep, not a feature. One commit. The
> changes are highly repetitive; the test surface is a single
> live `/ai-level local` re-score.

## Motivation

`/ai-level local` ran against 9 local skills produced this
table:

| Skill | Total | Weakest |
|---|---|---|
| ai-level | 8/8 | (none) |
| architecture-review | 8/8 | (none) |
| domain-info | 7/8 | Discernment |
| extend-skills | 7/8 | Discernment |
| skill-bootstrap | 7/8 | Discernment |
| demo | 6/8 | Discernment |
| enrich | 6/8 | Discernment |
| otel | 6/8 | Discernment |
| weather | 5/8 | Discernment + Delegation |

Project total: **60/72 (83%)**. Two skills already at full
marks — the rubric-applier itself and the architecture-review
skill, both designed under the BR-SKILL-013 discipline from
day one. Every other skill predates the rule and reflects the
implicit conventions of the time.

The rubric is fair: every weakness is a real one. Schema
markers in the body let a reader know "what does this skill
produce? in which schema, at which version?" without having to
run it. BR citations let a reader trace the rules a skill
implements. `weather`'s missing `disable-model-invocation`
ambiguates whether Claude can invoke it autonomously.

The fix per skill is < 5 lines of frontmatter + body. The
discipline is simply applying it across the existing skills.

## Per-skill audit

| Skill | Has BR | Has schema marker | Other gap |
|---|---|---|---|
| demo | ✓ (BR-EXTEND-010) | ✗ | — |
| enrich | ✗ | ✗ | — |
| otel | ✗ | ✗ | — |
| domain-info | ✓ (4 refs) | ✗ | — |
| extend-skills | ✓ (5 refs) | ✗ | — |
| skill-bootstrap | ✓ (5 refs) | ✗ | — |
| weather | ✗ | ✗ | `disable-model-invocation` not set |

The schema marker pattern is `<NAME> v<N>` (uppercase name +
space + v + digits, per `AiLevelChecker.SchemaVersionRegex`).
The most natural marker per skill:

| Skill | Schema marker reference | Why this one |
|---|---|---|
| demo | `DEMO_REPORT v1` | demo writes the report itself (BR-DEMO-004). |
| enrich | `OTLP v1` | enrich tags telemetry records that land as OTLP v1 in `telemetry.jsonl`. |
| otel | `DEMO_REPORT v1` (referenced) | `/otel up` precedes demo's report production. |
| domain-info | `DOMAIN_INFO v1` (introduced) | First version of the domain-info JSON shape. Add to BR-PROCESS-013 catalogue in this commit too. |
| extend-skills | `ARCHITECTURE_REVIEW v1` | extend-skills triggers the architecture-review at Phase 1.5. |
| skill-bootstrap | `PID_FILE v1` | skill-bootstrap manages the sidecar's PID file (registered in BR-PROCESS-015's catalogue). |
| weather | (no schema produced) | Weather emits free text — no schema. Body adds a one-line note: "WEATHER_OUTPUT v1 — free-text by design; schema versioning reserves the line for future structuring." |

## New / changed business rules

**No new BRs.** This plan applies existing rules
(`BR-SKILL-009`, `BR-SKILL-013`) to the existing skill
inventory; that's not a new rule, it's enforcement.

`BR-PROCESS-013`'s catalogue grows by one row (`DOMAIN_INFO v1`)
— the registry already accepts new schema entries by
addition; this is exactly the append-only discipline named in
Plan-10's amendment.

## Files affected

| Path | Change |
|---|---|
| `.claude/skills/demo/SKILL.md` | Body adds `DEMO_REPORT v1` reference. |
| `.claude/skills/enrich/SKILL.md` | Body adds BR citation (`BR-ENRICH-001`) + `OTLP v1` reference. |
| `.claude/skills/otel/SKILL.md` | Body adds BR citation (`BR-OTEL-001`) + `DEMO_REPORT v1` reference. |
| `.claude/skills/domain-info/SKILL.md` | Body adds `DOMAIN_INFO v1` reference. |
| `.claude/skills/extend-skills/SKILL.md` | Body adds `ARCHITECTURE_REVIEW v1` reference (at the Phase 1.5 line). |
| `.claude/skills/skill-bootstrap/SKILL.md` | Body adds `PID_FILE v1` + `PROMOTE_REPORT v1` references. |
| `.claude/skills/weather/SKILL.md` | Frontmatter adds `disable-model-invocation: false` (explicit); body adds `BR-OTEL-001` reference + `WEATHER_OUTPUT v1` schema-marker note. |
| `src/HelpersSidecar/Artefacts/ArtefactSpecs.cs` | Add `domain-info-response` spec for `DOMAIN_INFO v1` (registered for catalogue completeness; no writer — `/domain-info` returns the JSON inline, the spec entry is for visibility). |

No code logic changes. No test expectations change.

## Test approach

The deterministic rubric is the test:

1. Re-run `/ai-level local`.
2. Expected outcome: every skill scores **8/8**.
3. Project total: **72/72 (100%)**.

Per `BR-SKILL-013`, the rubric IS the test for skill quality.
The test target sits in the rubric runner, not in a new test
class.

If any skill still scores below 8/8 after this plan, the
plan-file's "Files affected" table is wrong and we iterate.
That's the safety check.

## Phase ordering

1. **Phase 1 (this commit)** — plan file. `plan:` prefix.
2. **Phase 1.5 — Architecture review.** Quick sweep — this
   plan is highly repetitive editing, almost certainly
   COMPATIBLE everywhere. Resolve any unexpected EXTENDS.
3. **Phase 2 — Implement.** All 7 skill edits + 1 ArtefactSpec
   addition in one commit. `feat(otel):` prefix. Verify via
   `/ai-level local` showing 72/72.
4. **Phase 3 — Build.** No-op (no .NET source changed beyond
   `ArtefactSpecs.cs`). `chore:` if rebuild needed.
5. **Phase 4 — Test.** Full suite; expect 377/377 unchanged
   (no test logic depends on SKILL.md body text). `test:`
   prefix.

## Rollback

`git revert <impl-commit>` restores the original SKILL.md
bodies. Each file's diff is small and self-contained.

## Out of scope

- **Tightening `weather`'s allowed-tools.** The current pattern
  `Bash(curl http://127.0.0.1:5050/skills/weather/dispatch *)`
  IS already the tight prefix per `BR-SKILL-009`. The 1/2
  Delegation score isn't from looseness — it's from the
  missing `disable-model-invocation` field. Setting that
  field explicitly to `false` (since weather IS designed to be
  model-invoked) closes the gap.
- **`DOMAIN_INFO v1` payload schema definition.** The spec
  entry registers the name; the actual JSON shape is whatever
  `DomainInfoDispatchEndpoint` currently returns. A future
  plan can formalise the shape into a typed record if
  consumers warrant it.
- **Cross-skill consistency on durability + reversal language.**
  Many skills already pass these checks; uniformity isn't the
  goal — each skill says what's true for it.

## Architecture review decisions

> BR-PROCESS-009 gate. `/architecture-review` runs against this
> plan in Phase 1.5; resolutions land here.

_(Awaiting Phase 1.5 — markers + resolutions land here.)_

## What kai-platform inherits

When `KaiPlatformDomain` lands, its skills MUST score 8/8 from
day one — the discipline this plan establishes for OTEL
applies uniformly across domains. `/ai-level local` will sweep
kai-platform's skills the same way.
