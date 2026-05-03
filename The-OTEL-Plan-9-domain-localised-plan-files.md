# Domain-localised plan files: move from project root → `docs/<domain>/plans/`

> Plan-9 follows Plan-5 (IDomain interface), Plan-6
> (architecture-review + gate), Plan-8 (demo report). Plan-9
> migrates plan files from the project root into per-domain
> subtrees so the file system mirrors the bounded-context map
> (Vernon, Implementing DDD, Ch. 2). Plan-9 is also the first
> real dogfood of `/architecture-review`'s gate (BR-PROCESS-009).
> Commit prefixes follow `BR-EXTEND-002`.

## Motivation

Three references in the OTEL domain's `TrustedReferences`
converge on the same answer: artefacts that use a domain's
ubiquitous language should live in that domain's subtree.

- **Eric Evans, *DDD Reference***: a Bounded Context delimits
  the applicability of a model; within it, terms have specific
  meaning.
- **Martin Fowler, "Bounded Context"** (`bliki/BoundedContext.html`):
  *"Software boundaries follow language boundaries."* Plan files
  are software artefacts that drive commits, gates, and schemas;
  their language is the domain's `Glossary`.
- **Vaughn Vernon, *Implementing DDD*, Ch. 2 (Domains, Subdomains,
  Bounded Contexts)**: each context retains its own canonical
  artefacts — its glossary, decision records, model documents
  — in its own subtree; the repository's directory structure
  mirrors the context map.
- **Microsoft Architecture Center, "Domain analysis"**: per-context
  subtrees for both code and docs in multi-bounded-context .NET
  solutions.

Today's structure puts all 9 OTEL plan files at the project root.
With kai-platform incubating at `/c/Work/kai-platform`, mingling
plans of different domains at the root is *exactly* the
boundary-bleed Evans / Fowler / Vernon warn against. Plan-9
moves OTEL's plans into `docs/otel/plans/` and updates
`PlanFileConventions` to honour the per-domain directory; future
domains plug into the same shape.

## New / changed business rules

- **`BR-EXTEND-004` amendment** — Plan numbering is per-domain,
  consecutive within each domain's `PlanFileConventions`.
  Numbering across domains is independent (kai-platform's plans
  start at 1, regardless of OTEL's count). Updated text reflects
  the per-domain directory.

- **`BR-EXTEND-006` amendment** — `IDomain.PlanFiles` gains a
  `Directory` property (relative to project root, e.g.
  `docs/otel/plans`). Consumers (`/extend-skills`, `/domain-info`,
  the `architecture-review` context loader) honour the directory.

- **`BR-PROCESS-001` amendment** — the playbook reference path
  updates from `.claude/skills/extend-skills/playbook.md` to
  `docs/<domain>/playbook.md`. The skill-side path stays as a
  stub linking to the domain's authoritative playbook.

## Files affected

### File moves (history-preserved via `git mv`)

| From | To |
|---|---|
| `The-OTEL-Plan.md` | `docs/otel/plans/The-OTEL-Plan.md` |
| `The-OTEL-Plan-2-go-collector.md` | `docs/otel/plans/The-OTEL-Plan-2-go-collector.md` |
| `The-OTEL-Plan-3-demo.md` | `docs/otel/plans/The-OTEL-Plan-3-demo.md` |
| `The-OTEL-Plan-4-...md` | `docs/otel/plans/The-OTEL-Plan-4-...md` |
| `The-OTEL-Plan-5-...md` | `docs/otel/plans/The-OTEL-Plan-5-...md` |
| `The-OTEL-Plan-6-...md` | `docs/otel/plans/The-OTEL-Plan-6-...md` |
| `The-OTEL-Plan-7-...md` | `docs/otel/plans/The-OTEL-Plan-7-...md` |
| `The-OTEL-Plan-8-demo-report.md` | `docs/otel/plans/The-OTEL-Plan-8-demo-report.md` |
| `The-OTEL-Plan-9-domain-localised-plan-files.md` | `docs/otel/plans/The-OTEL-Plan-9-domain-localised-plan-files.md` *(this file, after Phase 2)* |
| `.claude/skills/extend-skills/playbook.md` | `docs/otel/playbook.md` |

### Modified source

| Path | Change |
|---|---|
| `src/HelpersSidecar/Domain/PlanFileConventions.cs` | Gains `Directory` property (default `"."` for back-compat). |
| `src/HelpersSidecar/Domain/OtelDomain.cs` | `PlanFiles = new(Prefix: "The-OTEL-Plan", Directory: "docs/otel/plans")`. `PlaybookPath` updates to `docs/otel/playbook.md`. |
| `src/HelpersSidecar/Application/NextPlanFileName.cs` | Continues to take `existingFiles` (caller scopes the scan); no shape change. |
| `src/HelpersSidecar/Infrastructure/PlanDirectoryScanner.cs` | Accepts a directory parameter; scans the resolved domain's `PlanFileConventions.Directory` instead of `Directory.GetCurrentDirectory()`. |
| `src/HelpersSidecar/Endpoints/ExtendSkillsDispatchEndpoint.cs` | Resolves `domain.PlanFiles.Directory` and passes it to the scanner. The dispatch's "next plan file" output names the path-relative filename. |
| `src/HelpersSidecar/Endpoints/NextPlanNameEndpoint.cs` | Uses `domain.PlanFiles.Directory` (request body's `Root` becomes optional override). |
| `src/HelpersSidecar/Application/ArchitectureReviewContextLoader.cs` | Reads recent plans from `domain.PlanFiles.Directory` instead of project root. |
| `src/HelpersSidecar/Endpoints/DomainInfoDispatchEndpoint.cs` | The `plan-files` slice now serialises `directory`. |

### Modified docs

| Path | Change |
|---|---|
| `docs/business-rules.md` | Amend BR-EXTEND-004 / BR-EXTEND-006 / BR-PROCESS-001 text to reference per-domain plan locations. |
| `CLAUDE.md` | "Domains as a first-class concept" section gains the file-layout diagram. |
| `docs/process-incidents.md` | Append entry — "plan files were at root for too long; Plan-9 corrects". |

## Behavioural change

**Before:**
- All 9 OTEL plan files at `./*.md`.
- `/extend-skills otel <topic>` scans the project root.
- New OTEL plans land at the root.
- `/architecture-review` context loader reads recent plans from the root.

**After:**
- All 9 OTEL plan files at `docs/otel/plans/*.md`.
- `/extend-skills otel <topic>` scans `docs/otel/plans/`.
- New OTEL plans land in that directory.
- `/architecture-review` context loader reads from the resolved domain's directory.
- When kai-platform lands, its plans live at `docs/kai-platform/plans/` automatically — same scanner, different `IDomain.PlanFiles.Directory`.

## Test approach

- `PlanFileConventionsTests` — `Directory` defaults to `"."`; ` FileNameFor` ignores `Directory` (just generates the filename); the new test asserts the `Directory` is exposed.
- `OtelDomainTests` — assert `PlanFiles.Directory == "docs/otel/plans"`; assert `PlaybookPath == "docs/otel/playbook.md"`.
- `PlanDirectoryScannerTests` — scanning a populated temp dir vs an empty dir; the scanner walks the domain's directory.
- `ExtendSkillsDispatchEndpointTests` — `existing plans` line now names files in `docs/otel/plans/` (test sets up a fake scanner that returns the right names regardless of cwd; the assertion just checks the path-relative name renders).
- `DomainInfoDispatchTests` — `plan-files` slice JSON includes `directory`.

No new BRs; this is amendment-only across BR-EXTEND-004 / 006 + BR-PROCESS-001.

## Phase ordering

1. **Phase 1 (this commit)** — plan file. `plan:` prefix.
2. **Phase 1.5 — Architecture review.** `/architecture-review docs/otel/plans/The-OTEL-Plan-9-domain-localised-plan-files.md` (after the file moves; OR against the root path during this commit). Resolve any `EXTENDS` markers in the section below.
3. **Phase 2a** — `PlanFileConventions.Directory` property + `OtelDomain.PlanFiles` populates it. Tests. `feat(otel):` prefix.
4. **Phase 2b** — `PlanDirectoryScanner` honours per-domain directory. Endpoints (`extend-skills`, `domain-info`, `architecture-review-context-loader`) pass it through. Tests. `feat(otel):` prefix.
5. **Phase 2c** — File moves: `git mv` the 9 plan files + the playbook. `refactor:` prefix.
6. **Phase 2d** — Docs (BR amendments, CLAUDE.md, process-incidents). `docs:` prefix.
7. **Phase 3 — Build.** `chore:` if artefacts changed.
8. **Phase 4 — Test.** Full suite. `test:` prefix.

## Cross-domain discoverability — non-negotiable

> User constraint added 2026-05-03 mid-Plan-9: "As long as we
> don't lose the ability to find and understand cross-domain
> logic". Per-domain partitioning is desirable; partitioning that
> hides cross-cutting patterns is not.

This plan honours the constraint with three layers:

1. **Per-domain plans** live at `docs/<domain>/plans/`. OTEL's 9
   plans land at `docs/otel/plans/`. Future kai-platform plans
   land at `docs/kai-platform/plans/`. Each domain's plans are
   self-contained narratives for that bounded context.

2. **Cross-domain plans** live at `docs/cross-domain/plans/`.
   Reserved for plans that genuinely span domains: the `IDomain`
   contract itself, cross-domain BRs (BR-PROCESS-*), integration
   plans where two domains meet. Most plans are per-domain; this
   folder stays small but is the explicit answer to "where does
   the cross-cutting stuff live?". The sentinel domain
   `cross-domain` exists in the resolver as a first-class virtual
   domain — no skill, no playbook, no commit conventions; only a
   `PlanFiles.Directory`.

3. **Cross-domain BRs and incidents** stay at the existing
   project-root location: `docs/business-rules.md` and
   `docs/process-incidents.md`. These two files apply to every
   domain by definition (BR-PROCESS-001 governs `/extend-skills`
   regardless of domain). Putting them under any single domain
   would mis-place them. This honours the architecture review's
   QC concern.

**Discoverability mechanisms** (Phase 2b adds these):

- **Scanner endpoint takes optional domain filter.**
  `/helpers/plans/scan` (no arg) walks every known domain's
  `PlanFileConventions.Directory` plus `docs/cross-domain/plans/`,
  tags each result with its domain, returns the union.
  `/helpers/plans/scan?domain=otel` returns OTEL's only.
- **Auto-generated INDEX file.** `/helpers/plans/index` writes
  `docs/INDEX.md` — one section per domain, one line per plan,
  regenerated on demand. A human reading that one file gets the
  complete cross-domain picture; running the regen keeps it
  honest.

**What we explicitly preserve:**

- `git log --oneline --follow docs/` shows every plan-file
  evolution chronologically, regardless of domain.
- `grep -r "BR-PROCESS" docs/` finds every cross-domain BR
  reference in plans of any domain.
- The `/architecture-review` skill walks both per-domain and
  cross-domain plan directories when assembling its context.

This subsection raises a fourth EXTENDS-equivalent point — the
scanner endpoint's behaviour. Recorded below as
`BR-EXTEND-006-CROSS-DOMAIN`.

ARCHITECTURE_DECISION_REQUIRED:
  commitment: BR-EXTEND-006
  current:    scanner reads from a single project-root directory
  proposed:   scanner walks every domain's PlanFileConventions.Directory + docs/cross-domain/plans/, tags by domain

**Resolution: Evolve** — extend `/helpers/plans/scan` to walk
multiple directories with per-result domain tagging. Extend
`/helpers/plans/index` (new endpoint) to write a cross-domain
`docs/INDEX.md`. Default behaviour with no `domain` filter
preserves cross-domain visibility; the filter narrows when
needed. Justified by the user constraint and by the QC concern
the architecture review surfaced.

## Architecture review decisions

> BR-PROCESS-009 gate. `/architecture-review` was run against
> this plan on 2026-05-03 (Reviewer: Claude opus-4-7-1m, session
> `plan9-impl`). The schema-validated review identified three
> EXTENDS rows; the markers from the analyst's response are
> embedded verbatim below, paired with resolutions.
>
> The deterministic gate
> (`/helpers/plans/architecture-review-gate`) parses these
> entries to verify Phase 2 may proceed.

### EXTENDS markers from /architecture-review (verbatim)

ARCHITECTURE_DECISION_REQUIRED:
  commitment: BR-EXTEND-004
  current:    plan numbering is consecutive across the project
  proposed:   plan numbering is consecutive within each domain's directory

ARCHITECTURE_DECISION_REQUIRED:
  commitment: BR-EXTEND-006
  current:    IDomain.PlanFiles exposes Prefix + NumberFloor
  proposed:   IDomain.PlanFiles also exposes Directory (per-domain location)

ARCHITECTURE_DECISION_REQUIRED:
  commitment: BR-PROCESS-001
  current:    playbook lives at .claude/skills/extend-skills/playbook.md (skill-side)
  proposed:   playbook lives at docs/<domain>/playbook.md (domain-side)

### Resolutions

- BR-EXTEND-004 (Plan numbering is consecutive): **Resolution: Evolve** — amend the rule text to "consecutive within each domain's `PlanFileConventions`"; numbering across domains is independent. Justified by the bounded-context principle (Fowler's BoundedContext.html in TrustedReferences).
- BR-EXTEND-006 (Domains expose flow configuration via `IDomain`): **Resolution: Evolve** — extend `PlanFileConventions` with a `Directory` property; default `"."` preserves backward-compatibility for any domain not opting into per-directory storage.
- BR-PROCESS-001 (Skill changes go through `/extend-skills`): **Resolution: Evolve** — playbook path moves from `.claude/skills/extend-skills/playbook.md` to `docs/<domain>/playbook.md`. The skill remains the dispatcher; the playbook becomes the domain's authoritative narrative artefact.

The review's RECOMMENDATION was PROCEED. Other BRs in scope
(BR-EXTEND-005, BR-EXTEND-007, BR-EXTEND-008, BR-PROCESS-005,
BR-PROCESS-008, BR-PROCESS-013) were COMPATIBLE.

Two out-of-scope concerns the reviewer surfaced:

- QC: cross-plan-file references (e.g. Plan-5 mentioning "Plan-3")
  remain valid post-migration because filenames are unchanged.
  Worth a sweep before any future plan-file migration.
- QC: `docs/business-rules.md` and `docs/process-incidents.md`
  stay cross-domain. Re-evaluate the split when a 3rd domain
  materialises.

## Rollback

Each phase commits separately:

1. `git revert <plan-commit>` — drops Plan-9.
2. `git revert <2a-2d>` (in reverse order) — restores the
   project root layout. The 9 plan files come back via the
   reverted `git mv`s.
3. `git revert <3-chore>` / `<4-test>` if needed.

The history-preserving `git mv` means a revert is a clean
inverse; no manual file shuffling.

## Out of scope

- **Renaming the plan-file prefix.** OTEL plans stay
  `The-OTEL-Plan-N-<slug>.md`. The prefix is part of the
  `Glossary`'s ubiquitous language; changing it would invalidate
  every commit message that references a plan.
- **Auto-creating the `docs/<domain>/` tree at registration time.**
  Domain authors create their own subtree when they register the
  domain; tooling doesn't create directories implicitly.
- **Per-domain `business-rules.md` / `process-incidents.md`.**
  Both stay cross-domain at `docs/`. The domains' BR sets coexist
  in one file because cross-domain references are common (e.g.
  Plan-5's `BR-PROCESS-013` names patterns across multiple
  domains' implementations).
- **Per-domain `architecture-reviews/` capture.** Plan-6 has a
  forward-looking note about persisting reviews; if/when that
  lands it would naturally go to `docs/<domain>/architecture-reviews/`,
  but it's a separate plan.

## What kai-platform inherits for free

When `KaiPlatformDomain` lands:

```csharp
public sealed class KaiPlatformDomain : IDomain
{
    public string Name => "kai-platform";
    public PlanFileConventions PlanFiles { get; } = new(
        Prefix: "The-KaiPlatform-Plan",
        Directory: "docs/kai-platform/plans");
    public string PlaybookPath => "docs/kai-platform/playbook.md";
    // ...
}
```

The author creates `docs/kai-platform/plans/` + `docs/kai-platform/playbook.md`,
registers the singleton, and `/extend-skills kai-platform <topic>`
scans the right directory. Zero changes to existing OTEL code.
That's the test of the abstraction.
