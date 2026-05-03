# `/ai-level` — score skills against the AI-fluency 4 D rubric

> Plan-10 follows Plan-9 (per-domain plan files + cross-domain
> virtual domain + plans index). Plan-10 adds a domain-neutral
> `/ai-level` skill that scores any skill (or every local skill)
> against the four dimensions of AI fluency: **Delegation,
> Description, Discernment, Diligence**. Output is schema-
> versioned (`AI_LEVEL_REPORT v1` per `BR-PROCESS-013`).
>
> Plan-10 deliberately does NOT ship the artefact registry — that
> lands in Plan-11. The `/ai-level` writer hardcodes its output
> path (`output/ai-level/<UTC-ts>-<scope>.md`) following
> `BR-DEMO-004`'s shape; Plan-11 then retrofits this writer (and
> nine other existing producers) into a typed `IArtefactRegistry`.
> The fact that Plan-10 produces an unregistered artefact is the
> forcing function for Plan-11.

## Motivation

The Plan-9 retro surfaced a 4-dimensional read of where the
project sits on AI fluency: discernment and diligence are
strong (test ⇔ BR biconditional, schema-versioned reports,
per-phase commits), delegation is well-implemented but
under-documented as a single artefact, description is
structurally strong but lacks schema-checked task briefs at
Phase 2 entry. That read was qualitative and project-wide; we
have no way to ask "is THIS skill at level N?" or "which
skill has the weakest delegation story?".

A scoring skill answers both. Per-skill rows surface concrete
weaknesses (e.g. `weather`'s allowed-tools is `Bash(curl *)` —
too broad; `extend-skills`'s argument-hint is precise — strong).
Project aggregate surfaces cross-cutting weaknesses (e.g. "no
skill emits a structured task brief at Phase 2 entry" → an
ecosystem-level finding the qualitative retro couldn't surface
at scale).

The 4 D rubric is **Anthropic's published AI-fluency framework**
(Delegation, Description, Discernment, Diligence). User
explicitly chose this framework over alternatives. Future
frameworks could plug in via a new rubric type, but the v1
implementation pins to 4 D.

## New / changed business rules

- **`BR-SKILL-013` (new)** — Skills MUST be self-assessable
  against the 4 D rubric. The deterministic dimensions
  (delegation: presence of `disable-model-invocation`, tightness
  of `allowed-tools`; description: presence of `description` ≥ 50
  chars, presence of `argument-hint`, structured tools list;
  discernment: BR citations in body, schema-version markers;
  diligence: durable artefact production hint) are checked by the
  helpers sidecar. Judgement dimensions are scored by Claude
  reading the rubric output + the SKILL.md body.

- **`BR-PROCESS-013` extension** — `AI_LEVEL_REPORT v1` is added
  to the schema-versioned report catalogue. Layout documented in
  the "Output shape" section below.

No `BR-CODE-*` or `BR-EXTEND-*` changes. The artefact-registry-
related BRs (`BR-PROCESS-015`, `BR-SECURITY-004`) are deferred
to Plan-11.

## Files affected

### New source

| Path | Change |
|---|---|
| `src/HelpersSidecar/Application/AiLevelRubric.cs` | Pure-data 4 D rubric + deterministic-check definitions. |
| `src/HelpersSidecar/Application/AiLevelChecker.cs` | Runs the deterministic half of the rubric against a parsed `SkillFile`. Returns per-dimension pass/partial/fail with evidence. |
| `src/HelpersSidecar/Application/SkillFileParser.cs` | Reads a `SKILL.md`, parses YAML frontmatter, extracts body. Used by both the checker and the dispatch endpoint. |
| `src/HelpersSidecar/Application/AiLevelReportWriter.cs` | Composes `AI_LEVEL_REPORT v1` markdown. Writes to `output/ai-level/<UTC-ts>-<scope>.md`. Plan-11 will retrofit through `IArtefactWriter`. |
| `src/HelpersSidecar/Endpoints/AiLevelDispatchEndpoint.cs` | `POST /skills/ai-level/dispatch`. Enumerates target skills, runs the deterministic checker, composes the prompt Claude reads to score the judgement dimensions, writes the report. |
| `.claude/skills/ai-level/SKILL.md` | Domain-neutral skill. `allowed-tools: Bash(curl http://127.0.0.1:5050/skills/ai-level/dispatch *)`. Body emits the rubric output and asks Claude to complete the judgement scores. |

### New tests

| Path | Coverage |
|---|---|
| `tests/HelpersSidecar.Tests/Application/SkillFileParserTests.cs` | Parses well-formed frontmatter, missing frontmatter, malformed YAML, body extraction. |
| `tests/HelpersSidecar.Tests/Application/AiLevelCheckerTests.cs` | Each deterministic check independently; combinations; fixture skills representing pass/partial/fail per dimension. |
| `tests/HelpersSidecar.Tests/Endpoints/AiLevelDispatchEndpointTests.cs` | Single-skill mode, `local` mode, unknown-skill error, malformed args. |

### Modified source

| Path | Change |
|---|---|
| `src/HelpersSidecar/Program.cs` | DI: register parser, checker, report writer, dispatch endpoint. |
| `docs/business-rules.md` | Add `BR-SKILL-013`. |

### No file moves; no doc reorganisation; no skill renames.

## The 4 D rubric

Each dimension scored 0/1/2 (Absent / Partial / Strong) with
evidence. Per-skill total out of 8.

### Delegation (does the skill clearly carve work?)

**Deterministic checks (sidecar):**
- `disable-model-invocation` is set explicitly (true OR false; missing = ambiguous).
- `user-invocable` is set explicitly when the skill is non-default.
- `allowed-tools` is the tightest prefix that works: no bare `Bash`, no bare `Skill`, no `Bash(curl *)` without a host pin (`BR-SKILL-009`).

**Judgement (Claude):**
- Does the body explicitly call out what's deterministic vs what's judgement?

### Description (is intent communicated unambiguously?)

**Deterministic checks (sidecar):**
- `description` field present and ≥ 50 characters.
- `argument-hint` field present.
- `allowed-tools` is a structured list, not a single mega-pattern.

**Judgement (Claude):**
- Is the description specific enough to disambiguate triggering vs not? Does it mention the canonical user phrasing?

### Discernment (can the user verify the result?)

**Deterministic checks (sidecar):**
- Body cites at least one `BR-` (regex match).
- Body emits a schema-version marker (`<NAME> v<N>` regex).

**Judgement (Claude):**
- Does the body's example output enable a user to verify correctness without re-running the skill?

### Diligence (is work durable, attributable, correctable?)

**Deterministic checks (sidecar):**
- The body indicates durable artefact production (curl-to-sidecar pattern, `output/` write, commit prompt, or report-writer reference).
- The body indicates rollback / undo / inverse path (any of: `revert`, `discard`, `down`, `unset`, `undo`, `clear`).

**Judgement (Claude):**
- Does the body explain how to audit a past run (find the report, find the commits, replay the action)?

## Targeting

`/ai-level [target]`:

| `target` | Meaning |
|---|---|
| _(empty)_ | Usage + count of skills found in `.claude/skills/`. No side effects. |
| `<skill-name>` | One skill in the current project. Resolves against `.claude/skills/<name>/SKILL.md`. |
| `local` | Every skill under `.claude/skills/*/SKILL.md`. |

Plan-10 explicitly **does NOT support `global` or `all`**. Per
`BR-SECURITY-003` and the user's explicit decision earlier in
this design discussion, the sidecar reads only project-local
files. A future plan can add the global scope behind a startup
flag once the security boundary work lands.

## Output shape (`AI_LEVEL_REPORT v1`)

```
# AI level report — <scope>

> Auto-generated by `/ai-level <scope>`. Per `BR-PROCESS-013`
> this is a schema-versioned report.

AI_LEVEL_REPORT v1 — generated 2026-05-03T12:00:00Z
Scope: <scope>
Skills assessed: <N>

## Per-skill scores

| Skill | Delegation | Description | Discernment | Diligence | Total | Weakest |
|---|---|---|---|---|---|---|
| extend-skills | 2/2 | 2/2 | 2/2 | 2/2 | 8/8 | (none) |
| weather | 1/2 | 2/2 | 1/2 | 1/2 | 5/8 | Delegation, Discernment, Diligence |

## Per-skill evidence

### extend-skills (8/8)

**Delegation: 2/2**
- ✓ `disable-model-invocation: true` (explicit)
- ✓ `allowed-tools` is tight prefix (`Bash(curl http://127.0.0.1:5050/skills/extend-skills/dispatch *)`)
- ✓ Body calls out deterministic gathering vs Claude-driven flow

**Description: 2/2**
- ✓ Description = 412 chars; mentions every verb
- ✓ argument-hint = `<domain> [<topic> | revert | status]`
…

## Project aggregate

| Dimension | Average | Skills at 2/2 | Skills at 0/2 | Weakest example |
|---|---|---|---|---|
| Delegation | 1.7 / 2 | 5 / 7 | 0 / 7 | weather |
…

## Top 3 cross-cutting findings

1. **Two skills lack schema-version markers in body** (discernment): `weather`, `domain-info`. Concrete fix: add `<NAME> v1` to the body's example output section.
2. …

## How to act on this report

- Pick a skill with a weak dimension. Read its row's evidence.
- Open the SKILL.md; the evidence string names the missing
  field or pattern.
- The fix is usually < 10 lines. Re-run `/ai-level <skill>` to
  confirm the score improved.
```

## Test approach

- **`SkillFileParserTests`** — happy path, missing frontmatter,
  malformed YAML, body extraction, Windows line endings.
- **`AiLevelCheckerTests`** — each deterministic check
  independently with crafted fixture skills; one fixture per
  pass/partial/fail per dimension. Display names start with
  `BR-SKILL-013`.
- **`AiLevelDispatchEndpointTests`** — HTTP shape: usage form,
  single-skill form, `local` form, unknown-skill error,
  malformed args. Uses an in-memory fake parser to avoid disk.
- **No global-scope tests** — the feature isn't shipped.

The judgement-half of the rubric is NOT unit-tested — it's
Claude's call. The deterministic-half is the test target. This
matches `BR-SKILL-006` / `BR-SKILL-012` exactly.

## Phase ordering

1. **Phase 1 (this commit)** — plan file. `plan:` prefix.
2. **Phase 1.5 — Architecture review.** `/architecture-review docs/otel/plans/The-OTEL-Plan-10-ai-level.md`. Resolve any `EXTENDS` markers.
3. **Phase 2a** — `SkillFileParser` + `AiLevelChecker` + `AiLevelReportWriter` + dispatch endpoint + DI wiring + tests. `feat(otel):` prefix.
4. **Phase 2b** — `.claude/skills/ai-level/SKILL.md` + body. Self-test: `/ai-level ai-level` should score the skill itself. `feat(otel):` prefix.
5. **Phase 2c** — `BR-SKILL-013` text in `docs/business-rules.md`. The deterministic checks defined in this plan move into the BR's "Test target" section. `feat(otel):` prefix or `docs:` if no test changes (probably already covered by 2a's tests).
6. **Phase 3 — Build.** `chore:` if artefacts changed.
7. **Phase 4 — Test.** Full suite. `test:` prefix.

Each phase commits separately so any phase can be reverted
independently.

## Rollback

Standard `git revert <commit>` per phase. The skill directory
is new (`.claude/skills/ai-level/`), so reverting Phase 2b
removes the user-visible surface cleanly. Reverting Phase 2a
removes the sidecar machinery; the skill body would still exist
but couldn't dispatch (graceful degradation per
`BR-SKILL-010`).

## Out of scope

- **Artefact registry.** Plan-11. The `AiLevelReportWriter`
  hardcodes its output path; Plan-11 retrofits it (and nine
  other existing writers) through `IArtefactWriter`.
- **Global scope** (`/ai-level global` / `--all`). Future plan;
  needs `BR-SECURITY-NN` for the user-home filesystem boundary.
- **Cloud / remote destinations.** Plan-11+ once the registry
  exists.
- **Automated grading of the judgement scores.** A Claude that
  writes garbage but in the right schema currently passes; the
  human reading the report is the judge of the analysis itself.
  This matches `BR-SKILL-012`'s shape — analyst is Claude, but
  human is final judge.
- **Other AI-fluency rubrics** (other than 4 D). Pluggable
  rubric types are a future concern; v1 pins to 4 D per user's
  explicit choice.
- **CI integration.** A future plan can wire `/ai-level local`
  into a pre-commit or PR-check; Plan-10 just ships the skill.

## Architecture review decisions

> BR-PROCESS-009 gate. `/architecture-review` runs against this
> plan in Phase 1.5; resolutions recorded below.

_(Awaiting Phase 1.5 — markers + resolutions land here.)_

## What kai-platform inherits

When `KaiPlatformDomain` lands, `/ai-level` works on its skills
unchanged — the skill is domain-neutral, the rubric is
domain-neutral, and skills under `.claude/skills/` are
discovered regardless of which domain owns them. A future
domain may register additional rubric items (e.g. a
domain-specific delegation check); the rubric data type is
extensible by addition, not by replacement.
