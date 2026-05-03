# OTEL — Claude Code project guidance

This file is loaded into every Claude session in this directory. It
captures rules that apply to every change we make. Keep it short;
push detail into `The-OTEL-Plan.md`.

## Read first

- Full design: `./The-OTEL-Plan.md`.
- Cached skill conventions:
  `~/.claude/projects/C--Code-OTEL/memory/reference_skill_conventions.md`.
  Treat as authoritative for the duration of the turn. If the file's
  mtime is more than 7 days old, kick off a **background**
  refresh (Agent with `run_in_background: true`); do not block the
  user-visible response on it.

## Engineering practices: TDD + DDD with business-rule discipline

Apply where applicable — configuration files and SKILL.md don't
contain business rules in this sense, but the Go custom collector
components, the .NET helpers sidecar, and any non-trivial helper
logic do.

**The hard rule:** A test exists *if and only if* it proves a
**documented business rule**. Every business rule has at least one
test. CI rejects the PR if either side of that biconditional is
violated.

- Business rules live in `docs/business-rules.md`, each with a
  stable ID `BR-<AREA>-<NN>`. Areas: `ENRICH`, `OTEL`, `EXTEND`,
  `SKILL`, `HELPERS`, `SECURITY`. New areas need a justification.
- Test names start with the BR ID:
  `[Fact(DisplayName = "BR-ENRICH-001 — invalid keys rejected")]`
  in C#, `func TestProcessor_BR_ENRICH_004_DropsBatch…` in Go.
- If you can't name the rule, the test doesn't go in. Either
  document the rule first or delete the test.
- If you're adding a feature without a BR, you're adding it wrong.
- Bug fixes follow the same rule: amend or add the BR, write the
  test that would have caught it, then fix.

**DDD touchstones:**

- Three bounded contexts: telemetry pipeline (Go collector),
  session attribution (the control extension), skill operations
  (.NET helpers sidecar). Each communicates only via documented
  HTTP contracts.
- Use the project's ubiquitous language (session, enrichment,
  binding, topic, phase, collection — see The-OTEL-Plan.md for the
  full glossary). Don't invent synonyms.
- Value objects validate at construction; aggregates enforce
  invariants at their boundary.

## Skills are self-contained

Every file a skill needs lives inside `.claude/skills/<name>/`.
That includes helpers, templates, playbooks, business-rule
references, and any supporting markdown the skill body links to.
A skill never depends on a project-root file existing or having
particular content. Anything the skill needs from the rest of the
project is fetched at runtime via the documented HTTP APIs of the
two sidecars.

This is what lets a skill be copied into any project (or
`~/.claude/skills/`) and work the same way.

## Three questions to answer BEFORE creating or modifying any skill

Every change to a `SKILL.md` or its helper must answer all three. If
any answer is unclear, ask the user.

### 1. Minimum security permissions required

- What is the **smallest viable `allowed-tools` pattern**? Default
  is `Bash(node *)` because helpers are Node scripts. Anything wider
  needs a written justification.
- Which **exact file paths** does the helper read? Which does it
  write? Helpers may not write outside the project root,
  `~/.claude/`, or their own `${CLAUDE_SKILL_DIR}`.
- Which **network hosts/ports** does the helper hit? Default deny;
  loopback for our sidecars, plus an explicit allowlist for any
  external host.
- What **binaries** does the helper spawn (other than `node`)?
- Set `disable-model-invocation: true` if the skill should only ever
  be user-triggered. Set `user-invocable: false` if the skill is
  only reachable via chaining from another skill.

### 2. Minimum viable set of other skills this skill needs

- Does this skill chain to others via the `Skill` tool? List them
  explicitly in the SKILL.md frontmatter or body.
- If the answer is "none", say so — chaining is a real coupling.
  Only chain when capability isolation demands it (the canonical
  example is `/otel` → `/extend-skills` (renamed from `/otel-extend`
  in Plan-5 Phase 2c), where the broad capabilities live only on
  the chained skill).
- A chained skill must be loadable into Claude's context — i.e.
  `disable-model-invocation: false` (the default). `user-invocable:
  false` is the right combination for "reachable only via chain or
  direct typing".

### 3. No AI for deterministic work

- If the operation is **deterministic** (parsing, validation,
  scanning, slug normalisation, config probing, file I/O within
  documented paths, computation), the helper **calls the .NET
  deterministic-helpers sidecar** — it does NOT ask Claude to do it.
- Sidecar base URL: `http://127.0.0.1:5050`. OpenAPI spec at
  `/openapi.json`, Swagger UI at `/swagger`.
- Reserve Claude's judgement for tasks that genuinely require it
  (drafting prose, choosing approaches, summarising results).
- Adding a new deterministic operation means **adding an endpoint to
  the sidecar with OpenAPI annotations**, not embedding logic in a
  Node helper.

## Architecture summary

Two local services, both bound to `127.0.0.1`:

| Service | Tech | Port(s) | Owns |
|---|---|---|---|
| **OTel Collector** | Go (built via OCB) | `4318` (OTLP), `13133` (enrichment control), `13134` (healthz) | OTLP receive/process/export, per-session enrichment state, **persistent enrichments** loaded from `./persistent-enrichments.json` |
| **Deterministic Helpers** | .NET 10 minimal API (current LTS) | `5050` (HTTP + OpenAPI) | Deterministic operations skills need: plan-file scanning, slug normalisation, argument validation, config probing, git-status parsing |

Skill helpers are **thin HTTP clients** of one or both services.
They contain only the orchestration glue (parse argv, build a
request, print stdout) — no business logic.

## Skill rules anchored from the conventions doc

The cached conventions file is the source of truth. Local rules
that always apply:

- Single-quote `$ARGUMENTS` in every `!` shell-exec command:
  `!` `node '${CLAUDE_SKILL_DIR}/scripts/x.mjs' '$ARGUMENTS'`. Bash
  command substitution inside double quotes is an RCE primitive;
  single quotes neutralise it.
- Helpers receive user-supplied input via `process.argv[N]` — data,
  never code. Validate before using.
- All listening ports bind `127.0.0.1`. Public binding requires an
  explicit override flag and a startup banner.
- `disableSkillShellExecution` (managed setting) is honoured: the
  helper doesn't run; the skill prints a clear message.

## Two enrichment scopes

- **Persistent** — `/otel set <key>:<value>` / `/otel unset <key>` /
  `/otel config`. Backed by `./persistent-enrichments.json`. Applies
  to every session, survives collector restarts. Use for stable
  labels (team, env, cost-centre).
- **Per-session** — `/enrich <key> <value>`. In-memory only,
  isolated per `session.id`. Use for work-item context (ticket,
  feature flag).

The processor stamps persistent attributes first, then per-session
ones — per-session wins on key conflict.

## Justify every non-.NET dependency

.NET 10 is the project's primary runtime. Any non-.NET tool,
language, or runtime that lands in the repo MUST come with a
written justification stating what specifically .NET cannot do,
or what would be unreasonably costly in .NET. The justification
lives here and in `docs/business-rules.md` (BR-SKILL-008).

If the justification can't be written in two sentences, the
dependency can't be added.

**Current accepted non-.NET dependencies:**

- **Go (OCB-built OTel collector).** OCB only produces Go
  binaries; the upstream OpenTelemetry Collector is a Go
  project. We can't assemble a custom OTel-Collector
  distribution in .NET without recreating the entire collector
  framework, which is the opposite of the "build on standards"
  principle this project commits to.

- **`curl` in SKILL.md `!` exec.** SKILL.md preprocessing runs
  shell commands; we need *some* HTTP client in that one line
  to talk to the sidecar. `curl` ships with every supported
  platform (Windows 10+, macOS, Linux). The alternative — a
  per-platform .NET CLI shim — adds ~75 MB across five
  platforms in the repo. One line of `curl --data-urlencode` is
  the smallest possible bridge from a markdown skill into the
  sidecar.

Adding anything else (Python, PowerShell, Bash, Node, Rust)
requires updating this list AND adding a passing test that
exercises the dependency. No silent additions.

## `allowed-tools` patterns must be the tightest prefix that still works

Claude Code's permission grammar is **prefix-with-trailing-`*`**.
Every `allowed-tools` entry in a SKILL.md must name the longest
literal prefix that still lets the skill function. Two anti-patterns
to avoid:

- `Bash(curl *)` — too broad. Lets Claude curl any URL, any
  protocol, against any host. Replace with the exact endpoint:
  `Bash(curl http://127.0.0.1:5050/skills/<name>/dispatch *)`.
- `Skill` (bare) — too broad. Lets Claude invoke any skill via the
  Skill tool. Replace with the exact target:
  `Skill(otel-extend *)`.

Concretely, for the curl line to match a URL-prefix pattern, the
URL must be the **first** argument after `curl` (not after `-sS`),
because the permission system only matches at the start of the
command:

```
# matches Bash(curl http://127.0.0.1:5050/skills/<name>/dispatch *)
curl http://127.0.0.1:5050/skills/<name>/dispatch -sS --data-urlencode ...

# does NOT match — `-sS` precedes the URL
curl -sS http://127.0.0.1:5050/skills/<name>/dispatch ...
```

This is **defense in depth** — the `!` shell-exec preprocessing
line itself doesn't go through the permission system (it's pre-
prompt rendering). The tight `allowed-tools` pattern stops Claude
from making *additional* tool calls beyond the skill's intended
surface during subsequent turns.

This is captured as `BR-SKILL-009`. Reviewers reject any new
SKILL.md whose `allowed-tools` could be tighter.

## No third-language helpers — the sidecar is the boundary

The .NET deterministic-helpers sidecar is the **only** place
non-skill code lives. Skills are markdown plus a single shell
invocation (`curl` against the sidecar). We do NOT add helper
scripts in Node, Python, PowerShell, Go, or any third language
under `.claude/skills/<name>/scripts/`. The sidecar exists
precisely so we never have to.

If a skill needs new logic, the logic goes in the .NET sidecar
behind a new HTTP endpoint. The skill markdown becomes one more
line of `curl`.

Why: every helper script is a place where bugs live, security
patterns are duplicated, platforms diverge, and tests fragment.
The sidecar gives us one boundary, one schema (OpenAPI), one
runtime, one test surface. A third language for "just a small
helper" breaks all of those.

This is captured as `BR-SKILL-007` — see `docs/business-rules.md`.

## Skill changes go through `/extend-skills`

Changes to anything under `.claude/skills/**`, the
`Endpoints/*DispatchEndpoint.cs` files, or the `Application/*Verb.cs`
files MUST go through the `/extend-skills` flow (renamed from
`/otel-extend` in Plan-5 Phase 2c). That flow gates each phase
(plan → implement → build → test) with explicit user confirmation
and tracks every step under git so any phase can be reverted
independently.

Hand-rolled commits to those paths are forbidden, with named
bootstrap-class exceptions per `BR-PROCESS-001`. The currently
named exceptions are:

1. The commit that built `/otel-extend` (since renamed to
   `/extend-skills`) — the original flow couldn't govern its own
   creation.
2. The commit that built `/skill-bootstrap` — the deterministic-
   helpers sidecar at `:5050` had to come up before
   `/extend-skills` could dispatch through it.

Future bootstrap-class exceptions follow the same shape and must
be justified in `docs/process-incidents.md`.

Captured as `BR-PROCESS-001`. The historical incident that
prompted this rule is in `docs/process-incidents.md`.

### Correct procedure (the decision tree)

When the user asks for *any* change that touches:

- `.claude/skills/**`
- `src/HelpersSidecar/Endpoints/*DispatchEndpoint.cs`
- `src/HelpersSidecar/Application/*Verb.cs`
- (when in doubt about a path: default to "yes, route through")

then:

1. **Check whether `/extend-skills` has been built.** Look for
   `.claude/skills/extend-skills/SKILL.md` (post-Plan-5; pre-rename
   it was `.claude/skills/otel-extend/SKILL.md`).
   - **Not built →** propose the bootstrap exception: one named
     focused commit that builds `/extend-skills` end-to-end (skill
     dir + sidecar dispatch endpoint + tests). Get explicit user
     approval. Land that commit. Then return to step 1.
   - **Built →** continue to step 2.
2. **Don't edit the target file yet.** Tell the user we're
   entering `/extend-skills`. Either have them type
   `/otel extend <topic>` (canonical chain — emits the
   `EXTEND_REQUESTED: domain="otel" topic="..."` marker), or
   invoke `/extend-skills otel <topic>` directly if they prefer.
   The first arg to `/extend-skills` is the domain name; subsequent
   tokens are the topic (or the `revert` / `status` sub-verbs).
3. **Phase 0 (Pre-flight).** The flow checks `git status`. If
   the working tree is dirty, stop and have the user commit or
   stash. If there's no git repo, follow `BR-EXTEND-003`'s
   `git init` + baseline + double-confirm dance.
4. **Phase 1 (Plan).** Draft the change as
   `The-OTEL-Plan-<N>(-<slug>)?.md` (numbering from
   `BR-EXTEND-004`, slug from `BR-EXTEND-005`). Commit with the
   `plan: ` prefix (`BR-EXTEND-002`). Show the plan to the user
   and ask: "implement now?".
5. **Phase 2 (Implement).** Make the source changes. Show the
   diff. Ask: "commit?". Commit with `feat(otel): ` (or `fix:`,
   `refactor:`, etc., per the actual change kind).
6. **Phase 3 (Build).** If code changed, run the build. Surface
   any failure. On success, ask: "commit rebuilt artefacts?".
   Commit with `chore: ` if accepted.
7. **Phase 4 (Test).** Run the test suite. Show pass/fail.
   - All green → ask "keep / revert"; on keep, commit with
     `test: ` and end the flow.
   - Any failure → ask "revert / diagnose / keep with failing
     tests"; behave per choice.
8. **Revert is callable at any phase** via `/extend-skills <domain> revert`.
   Each phase committed separately so a single phase can be
   undone without disturbing the others.

The default answer when in doubt about whether to route a change
through `/extend-skills` is **yes, route it through**. The cost of
the flow is small; the cost of bypassing is invisible until the
next process incident.

## No magic strings or numbers in code

Anything that varies by environment, deployment, or future tuning
is a **setting**, not a string in code. URLs, hostnames, port
numbers, timeouts, retry counts, file paths, cache TTLs, resource
limits — all bound from `appsettings.json` onto typed options
classes and injected via `IOptions<T>`. Code references the typed
options, never a literal.

Allowed inline:
- HTTP method names (invariant by spec).
- JSON property names parsed from fixed external schemas (OTLP,
  OpenAPI, vendor APIs we don't own).
- Regex patterns that ARE the rule being enforced.
- Test fixture data scoped to one test class.
- User-facing message copy (v2 candidate for i18n) — but each
  message lives as one `const` per consumer, never duplicated.

A string in code is a code smell that signals missing config.
Captured as `BR-CODE-001`. Any new commit that adds a hardcoded
URL/host/port/timeout/path without the corresponding options
class and binding fails review.

## Retro after every requested change

After completing any user-requested change of meaningful scope
(a feature, a fix, a refactor, a non-trivial doc update), end
the response with a short retrospective covering:

1. **What happened** — what the change was, what friction came
   up, anything surprising.
2. **What could be improved** — process gaps, missed
   opportunities, things to do differently.
3. **Strategies for next time** — concrete actions or rules that
   would prevent the same friction or improve speed/quality.

Keep it tight: three sections, bullet points, ~200 words total.
No platitudes. If a strategy is generic ("communicate better"),
either make it concrete or drop it.

Substantial retros also append to `docs/retros.md` (newest entry
at top) so the operating history of the project is visible to
future contributors.

Captured as `BR-PROCESS-002`.

## Evidence-driven rule promotion (and demotion)

Strategies proposed in retros graduate to business rules when
they accumulate enough evidence. Rules that are repeatedly
violated despite enforcement get a forced review for possible
demotion. Both directions use the same machinery, parameterised
per skill or per strategy via a settable `evidence` block.

`evidence.stages` is an **ordered array**. Each element is a
required gate plus the minimum count of independent occurrences
that gate must clear before the strategy progresses to the next
stage. When the final stage's `min` is met, the strategy is
**promotable** — the next retro proposes it as a `BR-<AREA>-<NN>`.

**Default schema** (applied when nothing overrides it):

```yaml
evidence:
  stages:
    - gate: "concrete-and-testable"
      min:  1
    - gate: "applied-in-real-change"
      min:  3
    - gate: "no-rework-no-violation"
      min:  3
```

A strategy moves through the stages in order. Counts are tracked
visibly in `docs/retros.md` next to the strategy
(`stage[applied-in-real-change] 2/3`). No hidden state.

**Per-skill override** in SKILL.md frontmatter — replaces the
default for strategies whose primary surface is that skill:

```yaml
---
name: my-skill
description: ...
evidence:
  stages:
    - gate: "concrete-and-testable"; min: 1
    - gate: "applied"; min: 5
    - gate: "tests-pass"; min: 5
    - gate: "user-confirmed"; min: 5
---
```

**Per-strategy override** inline in `retros.md` — takes
precedence over skill-level and default. The full stages array
can be replaced, or a single stage's `min` / `gate` adjusted:

```markdown
- Strategy: "single-quote $ARGUMENTS in ! exec lines"
  evidence:
    stages:
      - gate: "applied"; min: 2
      - gate: "tests-pass"; min: 2
      - gate: "security-review-pass"; min: 1
  stage[applied] 1/2 in commit a932600
```

**Demotion** mirrors promotion. A BR with violation occurrences
at the same `min` (default 3) on the `applied` stage triggers a
**forced review** — not auto-demotion. The reviewer keeps,
fixes, or demotes.

**Quality gate.** Only strategies that pass the
`concrete-and-testable` stage can accumulate evidence. Generic
strategies ("communicate better") never advance past stage 1.
This is the firewall against retro noise.

Captured as `BR-PROCESS-003`.

## Evidence sources can be deterministic or HITL

Each gate in `evidence.stages` declares **where its data comes
from**. For deterministic gates (parsing, validation, presence
of an OTEL event, a passing test) the source is automatic and
the count is a fact. For judgement-based gates (does this
strategy actually feel like it's helping?) the source is a human
retro entry — HITL.

The `source` field on a gate selects the mechanism:

| `source`        | What it is                                                                                                                                                              | Counted by                                              |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| `hitl-retro`    | A human writes a retro entry attesting that the gate passed for this occurrence. Default when `source` is omitted.                                                     | Lines added to `docs/retros.md` referencing the gate.   |
| `otel-query`    | Run a structured query against the project's own OTEL output (`output/telemetry.jsonl`) and count matching records. Lets the system observe its own behaviour.        | The query result count.                                 |
| `ci-signal`     | A test or check passes in CI (or locally via `dotnet test --filter ...`). The named test must exist before the strategy can use this source.                          | Number of green runs of the named test.                 |
| `command-probe` | Run a command; exit 0 counts as a pass. Useful for "binary exists at X", "endpoint returns 200", and other plumbing checks.                                          | Number of zero-exit runs.                               |

Example using a deterministic OTEL-query gate (the user's worked
example: add an enrichment attribute to skill runs, then count
how many times the skill ran with it):

```yaml
evidence:
  stages:
    - gate: "concrete-and-testable"
      source: hitl-retro
      min: 1
    - gate: "skill-runs-with-marker"
      source: otel-query
      query:
        event: claude_code.skill_activated
        where:
          skill.name:    "my-skill"
          marker.tag:    "evidence-evidence-001"
        select: [event.timestamp, session.id, prompt.id, tool_input]
      min: 3
    - gate: "integration-test-validates-io"
      source: ci-signal
      test:  "MySkill.IntegrationTests.InputOutputContract"
      min:   1
```

For the OTEL query source specifically, the project's own
telemetry **is** its own evidence. A skill author tags
invocations of their strategy with a unique attribute (via
`/enrich` or a persistent attribute), and a retro queries the
local JSONL for matching events. The count is the number of
records, full stop.

Default source when nothing is specified: `hitl-retro`. This
keeps backward compatibility with `BR-PROCESS-003`'s default
schema and lets simple cases skip the parameterisation.

Captured as `BR-PROCESS-004`.

## Evaluate changes from ≥ 3 orthogonal perspectives

When recommending or evaluating any architectural change of
meaningful scope, surface pros and cons from **at least three
orthogonal perspectives** — genuinely different lenses, not three
sub-views of the same one.

Standard lens set (pick at least three; add more if relevant):

- **Engineering** — code we maintain, test coverage,
  language/toolchain burden, refactor cost.
- **Operations** — runtime, deployment, failure modes, operator
  familiarity, vendor docs alignment.
- **Strategy** — project alignment, future-proofing, ecosystem
  coupling, lock-in risk, optionality.
- **User-facing** — capabilities, ergonomics, edge-case
  behaviour.
- **Security** — threat surface, attack vectors,
  trust-boundary impact.
- **Cost** — engineering time, runtime resources, third-party
  fees, learning curve.

A "pros and cons" list with three sub-views of engineering does
NOT satisfy this rule. Single-perspective analysis hides whole
categories of loss until challenged.

Captured as `BR-PROCESS-006`.

## Flag significant architectural decisions, and document why we deviated

Any decision that introduces a new language, runtime, framework,
load-bearing library, deployment model, or storage shape MUST be
**flagged for explicit user confirmation** before it lands in
the plan or in code. "Significant" = expensive or disruptive to
reverse.

When flagging:

1. Name the decision in one sentence.
2. **Enumerate the alternatives — with research, not from
   memory.** If you haven't checked whether a viable alternative
   exists, say so before deciding.
3. State the trade-offs of each (the cost of being wrong, what
   it locks you into).
4. Recommend one with explicit reasoning.
5. Wait for user approval before proceeding.

**Once a path is chosen, also document the deviation.** If the
chosen path departs from a default, a documented standard, a
prior convention, or "what .NET-only would look like", that
departure is recorded in:

- **The commit message** that introduces the decision (one
  paragraph: what we deviated from, why, what we accept as the
  cost).
- **`docs/business-rules.md` or CLAUDE.md** — wherever the
  decision is load-bearing. For the Go-via-OCB choice, that's
  `BR-SKILL-008`'s exception list (the rule already requires
  this for non-.NET dependencies; `BR-PROCESS-005` generalises
  the requirement to all architectural deviations).

Worked counter-example: "OTel Collector in Go via OCB" was made
silently — I assumed Go was the only path because OCB is Go-only,
without checking whether a .NET service that re-implements the
small slice we actually need would be viable. (It would have
been.) The choice itself is defensible; the *process* was not,
and the deviation rationale was scattered across multiple
sections instead of being captured in one place at decision time.

The rule applies even when the chosen option is obviously right.
A two-line flag plus a one-paragraph deviation note costs
nothing; silent lock-in costs a pivot.

Captured as `BR-PROCESS-005`.

## Pre-conditions and installation policy

**Never install anything without explicit user consent.** This
applies to language runtimes (.NET, Node, Go), CLI tools (`ocb`,
`dotnet ef`), NuGet/npm/Go packages, binaries — anything that
changes the user's machine state.

Before any helper or skill installs a prerequisite, it MUST:

1. **Check** whether the prerequisite is already present at a
   satisfying version. Use the canonical version probe
   (`dotnet --version`, `node --version`, `go version`, etc.).
2. **Detect** whether it can be installed automatically on the
   current platform (winget on Windows, brew on macOS, the
   distro's package manager on Linux, or the language's own tool
   installer like `dotnet tool install -g`).
3. **Stop and ask.** If a prerequisite is missing, print:
   - what's missing (name + minimum version),
   - what would be installed (exact command, scope: user vs
     system, version that would land),
   - the upstream link the user can use to install it themselves.

   Then exit non-zero until the user re-runs with an explicit
   `--install` flag (or an equivalent confirmation).

This is the BR-SECURITY-003 rule — track it, test it, never bypass
it.

## Conventions for this project specifically

- New deterministic capabilities are added to the .NET sidecar with
  OpenAPI annotations, not as inline Node code.
- All sidecar endpoints (Go and .NET) bind `127.0.0.1`. No public
  exposure without a banner.
- Non-trivial changes to skills go through `/extend-skills`'s flow so
  every phase is git-checkpointed and revertable.
- Output JSONL files contain potentially sensitive enrichment values
  verbatim. Document this and warn users.

## Zero-downtime rebuilds (stage / promote / discard)

Plan-7 introduces a **green/blue** lifecycle on top of the basic
start/stop/sweep that every tier-managed component has. The
helpers sidecar opts in via its `StagingSpec`; future
tier-managed components can opt in by declaring their own
`Staging` slot on their `ComponentSpec`.

The three verbs (BR-PROCESS-011):

- `/skill-bootstrap stage` — build to `bin/Staging/`, spawn
  green on `:5051`. Blue keeps serving on `:5050`; OTEL stays
  continuous.
- `/skill-bootstrap promote` — atomic swap with rollback
  (BR-PROCESS-012). Snapshot blue → kill blue → copy staged
  binary → restart blue → verify. On verify-fail: restore from
  snapshot, leave green alive for inspection.
- `/skill-bootstrap discard` — kill green; leave blue alone.

The state machine never ends in "no blue, no green" except via
explicit user `discard + stop`. Promote failures leave at least
one viable instance.

For dev workflow this means: change C# code → `stage` (slow
build, no downtime) → `promote` (sub-second swap) → continue.
OTEL records carry the `plan` enrichment continuously; the
demo-report writer and the architecture-review agent both see
unbroken sessions.

## Lifecycle reports

Every multi-step lifecycle event in this project produces a
durable, human-readable, schema-versioned markdown report
(`BR-PROCESS-013`). Today's report-producing events:

- **Demo runs** → `output/demo-reports/<ts>-<domain>.md`
  (`DEMO_REPORT v1` — `BR-DEMO-004`). Per-step sections include
  the OTEL records emitted during each step's window.
- **Architecture reviews** (Plan-6) → the
  `ARCHITECTURE_REVIEW v1` schema is the response Claude emits;
  user records the resolution in the plan file.
- **Promote attempts** (Plan-7) → `PROMOTE_REPORT v1`
  documenting state transitions, timestamps, and rollback
  outcomes.

The pattern: **every lifecycle event a human might want to
review later is captured at the time of the event**. Schema
versioning means future changes to the report shape don't break
older reports' parseability.

## Architecture review and the evolution gate

Plan-6 introduces `/architecture-review` (Shape B — Claude as the
analyst) and `BR-PROCESS-009`'s evolution gate. Every plan-file
commit by `/extend-skills` triggers Phase 1.5: the user runs
`/architecture-review <plan-file>` and Claude emits a structured
review per the `ARCHITECTURE_REVIEW v1` schema. For every
commitment with `STATUS: EXTENDS`, Phase 2 (Implement) does not
proceed until the user records a resolution under the plan
file's `## Architecture review decisions` section. The four
resolution words:

- **Evolve** — amend the BR text; the change extends the
  architecture intentionally.
- **Constrain** — rework the plan to stay within the rule.
- **Defer** — capture the question as an open architectural
  item; do not land this change yet.
- **Override** — accept the deviation as a one-off with one-
  line justification.

The gate is enforced deterministically: `/extend-skills` calls
`POST /helpers/plans/architecture-review-gate` which scans the
plan file for `ARCHITECTURE_DECISION_REQUIRED` markers and
verifies each has a matching resolution. The check is pure-data
per `BR-SKILL-006`; the *judgement* (running the review) lives
in the user's Claude session per `BR-SKILL-012`.

`BR-EXTEND-009` adds plan-tagged sessions: every flow run
auto-emits a `/enrich plan <filename>` directive at Phase 0 so
every OTEL record from the session carries the plan attribute.
Per-plan filtering of `output/telemetry.jsonl` becomes one grep.

## Domains as a first-class concept

The project pivoted to multi-domain in Plan-5. Each project
domain (currently only `otel`; kai-platform incubating
externally at `/c/Work/kai-platform`) registers a singleton
`IDomain` implementation. Domain-aware skills resolve domains by
name through `IDomainResolver`.

**Per BR-EXTEND-006**, every `IDomain` exposes a typed
**knowledge facade** with these slices: `Name`, `PlanFiles`,
`Commits`, `GovernedGlobs`, `PlaybookPath`, `Glossary`,
`BusinessRulesPath`, `TrustedReferences`, plus optional
`Probe()` and `PorousBoundaries`. Each slice is owned by the
domain implementation; no central registry stores domain
content.

**Optional companion contract** `IDomainDemo` (`BR-EXTEND-010`)
lets a domain expose a guided demo. The dispatch endpoint
(`/skills/demo/dispatch`) owns the platform-level pre-flight +
teardown; the live skill-chain section is owned by the resolved
domain's `IDomainDemo.RunAsync`.

**Domain-aware skills** live as domain-neutral names with
`<domain>` as the first argument (per `BR-EXTEND-007`):

- `/extend-skills <domain> [<topic>]` — self-modification flow.
- `/demo [<domain>]` — guided onboarding tour (defaults to OTEL).
- `/domain-info <domain> [<slices>]` — read-only knowledge query
  over any subset of an `IDomain`'s slices.

**Adding a new domain** is one new class implementing `IDomain`
+ one DI registration in `Program.cs`. Optional: register a
companion `IDomainDemo`. No changes to existing consumers.

**Trusted references** (`BR-EXTEND-008`) are curated per-source
in each domain's `TrustedReferences`. The architecture-review
agent (Plan-6) will cite only URLs from this list. Adding a new
trusted reference is a `docs:` commit naming the source and
citing why; no blanket trust per host.

## Pointers

- Architecture, security guardrails, capability matrix:
  `./The-OTEL-Plan.md`.
- Skill convention rules (cached, refresh in background):
  `~/.claude/projects/C--Code-OTEL/memory/reference_skill_conventions.md`.
- OpenAPI spec for the helpers sidecar:
  `http://127.0.0.1:5050/openapi.json` (when running);
  static copy at `./src/HelpersSidecar/openapi.json` for offline
  reference.
