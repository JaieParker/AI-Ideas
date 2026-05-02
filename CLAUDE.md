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
  example is `/otel` → `/otel-extend`, where the broad capabilities
  live only on the chained skill).
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
- Non-trivial changes to skills go through `/otel-extend`'s flow so
  every phase is git-checkpointed and revertable.
- Output JSONL files contain potentially sensitive enrichment values
  verbatim. Document this and warn users.

## Pointers

- Architecture, security guardrails, capability matrix:
  `./The-OTEL-Plan.md`.
- Skill convention rules (cached, refresh in background):
  `~/.claude/projects/C--Code-OTEL/memory/reference_skill_conventions.md`.
- OpenAPI spec for the helpers sidecar:
  `http://127.0.0.1:5050/openapi.json` (when running);
  static copy at `./src/HelpersSidecar/openapi.json` for offline
  reference.
