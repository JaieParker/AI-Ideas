# /architecture-review skill (Shape B) + BR-PROCESS-009 evolution gate + BR-EXTEND-009 plan-tagged sessions

> Plan-6 follows Plan-5 (`56dab2b` + `83d2035`). Plan-5 introduces
> the `IDomain` interface + the `/otel-extend` → `/extend-skills`
> rename + `TrustedReferences` curation. Plan-6 builds on it: a
> qualitative architecture-review skill, an explicit human-gate
> on architecture evolution, and per-plan OTEL tagging so each
> plan's activity is queryable.
> Commit prefixes follow `BR-EXTEND-002`.

## Motivation

Three concerns the project has surfaced but doesn't yet enforce:

1. **No deliberate review of architectural fit.** Plans get drafted
   and implemented; whether they fit the project's architectural
   commitments depends on the author noticing. There is no
   structured gate that asks "does this fit, extend, or violate
   the established architecture?".

2. **No human-decision point when architecture evolves.** When a
   plan implies the architecture should change (a new pattern, a
   new boundary, a contradiction with a prior `BR-*`), today the
   change is implemented and the contradiction is discovered later.
   `BR-PROCESS-005` asks for the deviation to be flagged; nothing
   *enforces* that the user gets to decide.

3. **No per-plan filter on the project's own telemetry.** Records
   in `output/telemetry.jsonl` carry session and ticket attributes,
   but not the plan. Querying "which records relate to Plan-4?"
   requires session-id archeology. With multiple plans active in
   parallel (Plan-5 implementation + Plan-6 design + occasional
   hot-fix), this becomes an audit burden.

Plan-6 closes all three:

- **`/architecture-review`** — a qualitative-judgement skill that
  loads context and renders a structured prompt for Claude (the
  user's session) to produce a typed review (the "Shape B"
  pattern decided in conversation).
- **`BR-PROCESS-009`** — the human-decision gate. When review
  output flags any commitment as `EXTENDS`, Phase 2 of
  `/extend-skills` refuses to proceed until the user explicitly
  picks Evolve / Constrain / Defer / Override.
- **`BR-EXTEND-009`** — every plan-implementation session
  auto-enriches with `plan:<full-filename>` so the project's own
  telemetry can be filtered per plan.

## New / changed business rules

- **`BR-PROCESS-009` — Architecture evolution requires explicit
  human decision.** When `/architecture-review` reports any
  commitment with `STATUS: EXTENDS`, the `/extend-skills` flow
  MUST gate Phase 2 (Implement) on a recorded human decision:

  - `Evolve` — amend the affected `BR-*` text (and any
    consequent CLAUDE.md sections); the plan extends the
    architecture intentionally.
  - `Constrain` — rework the plan to stay within current
    commitments; re-run `/architecture-review`.
  - `Defer` — capture the question as an open architectural
    item; the change does not land in this plan.
  - `Override` — accept the violation as a one-off with a
    one-line justification recorded in the plan; useful for
    deliberate one-offs that don't justify a rule change.

  The decision lands in the plan file's new "Architecture review
  decisions" section (per BR-PROCESS-005; this rule adds the
  *gate* — BR-PROCESS-005 already requires the *flag*).

- **`BR-SKILL-012` — `/architecture-review` is purely qualitative
  judgement (Shape B).** The skill MUST NOT contain deterministic
  per-commitment checks. Mechanical checks (BR↔test biconditional,
  prefix tightness, schema validation) belong in lint test classes
  and remain there. `/architecture-review` is exclusively
  Claude-as-analyst; the dispatch loads context and renders a
  prompt with a strict response schema, and the skill body
  enforces only the response shape (post-processing).

  *Why:* `BR-SKILL-006` carves deterministic work to the sidecar
  and judgement work to the LLM. Architectural review is
  judgement. Deterministic-check coverage drifts toward
  what's-easy-to-encode and creates false confidence in shallow
  greens. The lint suite catches mechanical drift; the
  architecture-review skill catches qualitative drift; the two
  jobs live in two places.

- **`BR-EXTEND-009` — Plan-implementation sessions are tagged with
  the plan filename.** When `/extend-skills <domain> <topic>` is
  invoked (Phase 0 / pre-flight), the flow MUST set the
  per-session enrichment `plan:<full-plan-filename>.md` via the
  collector control API (the same path `/enrich` uses).

  Every OTEL record emitted during the plan's life — drafting,
  implementation, build, test, retro — carries this attribute.
  Per-plan filtering becomes one grep:

  ```bash
  grep '"plan":"The-OTEL-Plan-6-...md"' output/telemetry.jsonl
  ```

  When work happens **outside** `/extend-skills` (a manual
  hot-fix, a plan-less commit), the user runs `/enrich plan
  <filename>` themselves before the work. The architecture-review
  agent's rendered prompt cites the value of `plan` from the
  current session's enrichment so its findings tie back to the
  exact plan that triggered them.

  *Why:* per `BR-PROCESS-004`, the project's own telemetry is its
  evidence. Per-plan filtering lets retros, audits, and the
  architecture-review agent query exactly the activity for one
  plan without session-id archaeology. The cost is one extra
  POST per session start; the benefit compounds over the project
  lifetime.

- **`BR-PROCESS-001` text amendment.** The "skill changes go
  through `/extend-skills`" rule grows a sentence: *"Phase 1.5
  (Architecture review) runs `/architecture-review` against the
  draft plan. The user resolves any `EXTENDS` markers per
  `BR-PROCESS-009` before Phase 2 begins."*

- **`BR-PROCESS-005` text amendment.** The architectural-decision
  flag now lands in the plan file's "Architecture review
  decisions" section as a structured artefact, not just commit-
  message prose. The commit message still summarises; the plan
  file holds the canonical record.

## Files affected

### New files

| Path | Purpose |
|---|---|
| `src/HelpersSidecar/Application/ArchitectureReviewVerb.cs` | Verb parser. Args: `<target>` (required: file path \| plan-id \| `current` for uncommitted diff \| branch name); optional `--domain=<name>` to override domain inference. |
| `src/HelpersSidecar/Application/ArchitectureReviewContextLoader.cs` | Composes the prompt context: CLAUDE.md, `docs/business-rules.md`, recent plans, the target body, the resolved `IDomain`'s slices (especially `TrustedReferences`), `docs/process-incidents.md`, the active session's `plan` enrichment value. |
| `src/HelpersSidecar/Endpoints/ArchitectureReviewDispatchEndpoint.cs` | Loads context via `ArchitectureReviewContextLoader`, renders the structured prompt, returns text for Claude to read. The dispatch is the API; Claude is the analyst. |
| `.claude/skills/architecture-review/SKILL.md` | User-only skill (`disable-model-invocation: true`). Curls the dispatch endpoint; the skill body instructs Claude to read the rendered prompt and emit the response per the schema in `schema.md`. Includes the post-processing validator (does the response match the schema? if not, retry). |
| `.claude/skills/architecture-review/HELP.md` | Verb reference. |
| `.claude/skills/architecture-review/schema.md` | The `ARCHITECTURE_REVIEW v1` schema (full spec, copied below). |
| `tests/HelpersSidecar.Tests/Application/ArchitectureReviewVerbTests.cs` | Argument parsing — target shape, domain override, missing-arg failure. |
| `tests/HelpersSidecar.Tests/Endpoints/ArchitectureReviewDispatchTests.cs` | Context assembly correctness. Mocks `IDomainResolver` + `IFileSystem` (or uses fixture files). Asserts: (1) the rendered prompt cites `TrustedReferences` only from the resolved domain; (2) the prompt includes recent BRs in scope; (3) when the target is a plan file, recent plans are loaded for context; (4) the schema document is rendered into the prompt verbatim. |
| `docs/architecture-review-template.md` | Stub for the "Architecture review decisions" section that gets appended to plan files (so contributors copy/paste from a known shape). |

### Modified files

| Path | Change |
|---|---|
| `src/HelpersSidecar/Application/ExtendSkillsVerb.cs` | New phase enum value `Phase1_5_ArchitectureReview` between `Phase1_Plan` and `Phase2_Implement`. The flow auto-invokes `/architecture-review` after Phase 1's plan-file commit lands and before prompting "implement now?". |
| `src/HelpersSidecar/Endpoints/ExtendSkillsDispatchEndpoint.cs` | New verb `architecture-review` (or chained-via-marker — TBD in Phase 2). New behaviour at Phase 0 / pre-flight: call `/skills/enrich/dispatch` with `args=plan <plan-filename>` to satisfy `BR-EXTEND-009`. |
| `.claude/skills/extend-skills/playbook.md` | New Phase 1.5 section. Phase 0 also gains a sentence on the `BR-EXTEND-009` auto-enrichment. |
| `.claude/skills/extend-skills/templates/plan-template.md` | New section "Architecture review decisions" with the `BR-PROCESS-009` decision shape (Evolve / Constrain / Defer / Override). |
| `.claude/skills/extend-skills/SKILL.md` | Body instruction added: "After Phase 1's plan commit, invoke `/architecture-review <plan-file>`. Render the response. If any commitment status is `EXTENDS`, gate on the user's decision per `BR-PROCESS-009` before proceeding to Phase 2." |
| `src/HelpersSidecar/Domain/IDomain.cs` | Optional new slice `IReadOnlyList<string> ArchitecturalCommitments { get; } => Array.Empty<string>();` — list of BR IDs in scope for this domain. Default empty (review uses all BRs); domains can scope. |
| `src/HelpersSidecar/Domain/OtelDomain.cs` | Populates `ArchitecturalCommitments` with the BR IDs the OTEL domain commits to (BR-ENRICH-*, BR-OTEL-*, BR-EXTEND-*, BR-SKILL-*, BR-HELPERS-*, BR-PROCESS-*, BR-DEMO-*, BR-CODE-*, BR-SECURITY-*). |
| `docs/business-rules.md` | New BR-PROCESS-009. New BR-SKILL-012. New BR-EXTEND-009. Amendments to BR-PROCESS-001 and BR-PROCESS-005. |
| `CLAUDE.md` | New section "Architecture review and the evolution gate" describing the flow and the four resolution choices. Cross-link to schema.md. |
| `docs/process-incidents.md` | Append entry — the "no architecture-fit gate" failure mode this plan closes. |

## The `ARCHITECTURE_REVIEW v1` schema

Copied from `schema.md`; the dispatch endpoint renders this verbatim into the prompt so Claude follows it.

```text
ARCHITECTURE_REVIEW v1
======================
Target:        <what was reviewed (path or plan-id)>
Date:          <UTC timestamp>
Domain:        <name from IDomainResolver>
Plan tag:      <value of session enrichment "plan", if any>
Reviewer:      Claude (model: <model id>; session: <session id>)

PER-COMMITMENT EVALUATION
-------------------------
<For every BR-* in scope>:
  <BR-ID> (<short title>)
    STATUS:    COMPATIBLE | VIOLATES | EXTENDS
    REASONING: <≤3 sentences>
    CITED:     <0..N URLs from the domain's TrustedReferences;
                use "(none)" when no external citation applies>

ARCHITECTURAL DECISIONS REQUIRED
--------------------------------
<None | one or more entries of the form:>
  ARCHITECTURE_DECISION_REQUIRED:
    commitment: <BR-ID>
    current:    <one-sentence summary of the existing rule>
    proposed:   <one-sentence summary of how the change extends it>
    options:
      Evolve    — amend the BR text and consequent CLAUDE.md sections.
      Constrain — rework the plan to stay within the current rule.
      Defer     — capture as open question; do not land this change yet.
      Override  — accept the deviation as a one-off with one-line
                  justification recorded in the plan.

OUT-OF-SCOPE CONCERNS
---------------------
<Any qualitative observations the reviewer raised that no current
 BR covers. Each is one paragraph; flag with QC: prefix.>

RECOMMENDATION
--------------
PROCEED | EVOLVE_FIRST | CONSTRAIN | DEFER | DISCUSS

REVIEWER NOTES (optional)
-------------------------
<Free-form. May reference TrustedReferences. May include suggested
 BR text amendments if the reviewer feels strongly about an Evolve.>
```

The skill body's post-processing validates: every BR in scope has a `STATUS`; every `STATUS` value is one of the enum; every `EXTENDS` row has an `ARCHITECTURE_DECISION_REQUIRED` block; every `CITED` URL appears in the resolved domain's `TrustedReferences`. If validation fails, the body instructs Claude to retry with the schema enforced.

## Behavioural change

**Before:**

- A plan drafts and commits; nothing reviews it for architectural fit.
- Architectural extensions land silently — discovered weeks later when a contradiction surfaces.
- Per-plan analysis of OTEL records requires reconstructing which session worked on which plan from git history.
- `BR-PROCESS-005`'s "flag and document" requirement is honoured by author discipline alone.

**After:**

- `/extend-skills` auto-runs `/architecture-review <plan-file>` between Phase 1 (plan commit) and Phase 2 (implement).
- The architecture-review output is structured, schema-validated, and committed as a section of the plan file.
- `EXTENDS` markers gate Phase 2 — the user picks one of four resolutions before any code lands.
- Every OTEL record produced during the plan's life carries a `plan` attribute. `grep '"plan":"<filename>"' output/telemetry.jsonl` is the canonical query.
- The architecture-review skill's prompt cites only URLs from the resolved domain's `TrustedReferences` (BR-EXTEND-008). The list is auditable.

## Test approach

Per `BR-PROCESS-007` every test scopes to one domain change:

- **`ArchitectureReviewVerbTests`** — argument parsing for the four target shapes (file path, plan-id, `current`, branch); domain override; missing-target failure.
- **`ArchitectureReviewDispatchTests`** —
  - context assembly: rendered prompt contains every relevant BR-ID, the schema, the resolved domain's `TrustedReferences`, and the active session's `plan` value (mocked);
  - prompt does NOT contain BR-IDs outside the resolved domain's `ArchitecturalCommitments` (when the slice is populated);
  - target-not-found returns 404;
  - unknown domain returns 404.
- **`ExtendSkillsPhase15Tests`** — verifies `/extend-skills` (post-rename) injects the architecture-review chain at the right phase. Mocked Claude response is asserted on; if mocked response contains `STATUS: EXTENDS`, the flow refuses to proceed without a recorded decision; if all `COMPATIBLE`, the flow proceeds.
- **No tests of Claude's analytical output itself.** That output is qualitative and varies per run. Tests assert the *structural* shape of the response, not its content.

The existing test suite (currently 193/193) gains ~15-20 new tests. All domain-scoped per `BR-PROCESS-007`. None of them exercise live LLM calls — Claude's response is mocked at the dispatch boundary in tests; live runs happen in user sessions only.

## Phase ordering

1. **Phase 0 — pre-flight enrichment.** As part of THIS plan's
   own implementation: `/enrich plan The-OTEL-Plan-6-architecture-
   review-and-evolution-gate.md`. This is the dogfooding moment
   for `BR-EXTEND-009`. (Captured here; the enrichment is set
   when the platform is brought back up.)

2. **Phase 1 (this commit)** — plan file. `plan:` prefix.

3. **Phase 2a** — Skill + dispatch endpoint scaffolding. Verb parser,
   context loader, dispatch endpoint, schema document, skill markdown.
   Dispatch returns the rendered prompt; no integration with
   `/extend-skills` yet. `feat(architecture-review):` prefix.

4. **Phase 2b** — `BR-EXTEND-009` auto-enrichment in `/extend-skills`'s
   Phase 0. Adds the `/skills/enrich/dispatch` call to the
   pre-flight section. `feat(extend-skills):` prefix.

5. **Phase 2c** — `/extend-skills` Phase 1.5 integration. After
   Phase 1's plan commit, the flow chains to `/architecture-review`,
   parses the response, gates Phase 2 on any `EXTENDS` markers,
   records the decision in the plan file. `feat(extend-skills):`
   prefix.

6. **Phase 2d** — `BR-PROCESS-009`, `BR-SKILL-012`, `BR-EXTEND-009`
   land in `docs/business-rules.md`. `BR-PROCESS-001` and
   `BR-PROCESS-005` text amendments. `CLAUDE.md` section.
   `docs/process-incidents.md` entry. `docs:` prefix.

7. **Phase 2e** — Plan template gains "Architecture review
   decisions" section. `docs/architecture-review-template.md`
   stub. `docs:` prefix.

8. **Phase 3 — Build.** `dotnet build`; `chore:` prefix only if
   artefacts changed.

9. **Phase 4 — Test.** Full suite + manual smoke: invoke
   `/architecture-review` against this Plan-6 file itself
   (dogfood), confirm the schema validates, confirm the agent
   correctly identifies that Plan-6 is COMPATIBLE with current
   architecture (it shouldn't flag itself as EXTENDS — Plan-6
   IS the architecture extension that adds the gate, but that
   change has already been documented as the plan's purpose).
   Edge case: at the moment Plan-6 is reviewed, the gate doesn't
   yet exist; the review runs without enforcement. That's
   correct — bootstrap. Subsequent plans run with enforcement.
   `test:` prefix.

10. **Phase 5 (acceptance, optional)** — `/architecture-review`
    invoked against an existing plan (e.g. Plan-4 or Plan-5)
    to confirm a retrospective review produces the expected
    schema-shaped output.

## Rollback

Each phase commits separately; revert any individually:

1. `git revert <plan-commit-sha>` — drops Plan-6.
2. `git revert <2a-2e>` (in reverse order) — drops the
   implementation phases.
3. `git revert <3-chore-sha>` — drops rebuilt artefacts.
4. `git revert <4-test-sha>` — drops the test pass marker.

Reverting Phase 2c (`/extend-skills` Phase 1.5 integration)
specifically removes the gate but leaves the `/architecture-review`
skill standalone — users can still invoke it manually. The skill
becomes optional; the rule reverts.

## Out of scope

- **Live-LLM CI gating.** Running `/architecture-review` in CI
  against every PR would require either (a) capturing Claude's
  response in the PR (committed) or (b) calling Claude from CI
  (cost + non-determinism). v1 runs the review in the user's
  session during `/extend-skills` and commits the captured
  response to the plan file. CI verifies the response is
  *present and well-formed*; it doesn't re-run the analysis.

- **Cross-plan architectural drift detection.** The agent reviews
  one change at a time. Detecting "five plans collectively
  drifted the architecture even though each was individually
  COMPATIBLE" requires temporal analysis. Future plan if needed.

- **Auto-amending BR text on Evolve.** When the user picks Evolve,
  the agent could auto-draft the BR amendment. v1 leaves this to
  the user — the Evolve resolution captures the *decision*; the
  amendment is a follow-up `docs:` commit.

- **`/architecture-review` reviewing itself.** The bootstrap case
  at Phase 4 is a one-time thing; we don't build self-review
  recursion.

- **Multi-domain reviews in one call.** The agent reviews one
  domain per invocation. Cross-domain plans (e.g. one that
  affects both `otel` and a future `kai-platform`) run the
  review twice. Future enhancement: union of domains.

- **Architecture review for `/skill-bootstrap` changes.** The
  platform skill is intentionally domain-independent; whether
  `/architecture-review` should evaluate platform changes (and
  against what commitments) is its own design question. v1
  leaves the platform skill outside the gate.

- **Recording Claude's exact model id and reproducibility
  metadata.** v1 logs the model name in the schema header
  (`Reviewer: Claude (model: ...)`). Capturing the deterministic
  inputs to enable re-running is a v2 hardening.

## What this dogfoods

When `/extend-skills otel architecture-review-and-evolution-gate`
runs to implement Plan-6:

1. Phase 0 sets `/enrich plan The-OTEL-Plan-6-...md` (dogfooding
   `BR-EXTEND-009`).
2. Phase 1 commits this plan file (already done).
3. Phase 1.5 — *can't run yet because the skill doesn't exist*.
   First-time bootstrap: the user manually reviews this plan file,
   notes any `EXTENDS` qualitatively, picks resolutions if
   needed. Subsequent plans run through the agent.
4. Phase 2a-2e implement.
5. Phase 4 (acceptance) runs `/architecture-review` on a *prior*
   plan to demonstrate the agent works retrospectively. From the
   next plan onward, the gate applies on every entry to Phase 2.

This is the same bootstrap-exception pattern as `/otel-extend`
and `/skill-bootstrap`: the skill that introduces a rule cannot
itself be governed by that rule on the way in. One named
exception per rule, justified in `docs/process-incidents.md`.

## What kai-platform inherits for free

When `KaiPlatformDomain : IDomain` lands:

- Its `ArchitecturalCommitments` slice scopes `/architecture-review`
  to kai-platform-specific BRs.
- Its `TrustedReferences` provides citation sources distinct from
  OTEL's.
- Plan-files for kai-platform get auto-tagged with `plan:` per
  `BR-EXTEND-009`.
- Phase 1.5 review applies identically.

The plan-6 changes are domain-agnostic by construction; one
implementation, every domain consumes it.
