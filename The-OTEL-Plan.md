# Live OTEL enrichment for Claude Code — design

## At a glance

A custom OpenTelemetry Collector distribution that lets a developer
attach arbitrary `{key, value}` attributes to every span, log, and
metric Claude Code emits in the current session, mutate that set
mid-session via a slash command, and have it scoped per session so
multiple Claude windows don't collide. The build is a stock OTel
Collector plus one custom processor and one custom extension; the
slash command talks to the extension's HTTP control API. Output goes
to a local file by default and to any OTLP backend by adding one line
to `config.yaml`.

A second local service — a small **.NET 10 deterministic-helpers
sidecar** — hosts every deterministic operation a skill needs (plan-
file scanning, slug normalisation, argument validation, config
probing). Skills' Node helpers are thin HTTP clients of both
services and contain no business logic. The .NET sidecar exposes an
**OpenAPI** spec so anyone adding a skill can see the contract.

The project ships pre-built binaries alongside source so that
`git clone <repo> && claude` then `/otel` is the entire bring-up.
A second slash command, `/enrich`, manages the per-session attribute
map. A third example skill, `/weather`, demonstrates the pattern.
A fourth verb, `/otel extend`, lets the project safely modify itself
under git.

## The problem

Claude Code emits OTEL telemetry but offers no first-party way to tag
it with the work-item context a developer cares about (a ticket, a
feature flag, an experiment name). The two standards-blessed options
both fail the requirement:

- `OTEL_RESOURCE_ATTRIBUTES` is read once at process start and frozen
  for the life of the `claude` process —
  [Resource SDK spec](https://opentelemetry.io/docs/specs/otel/resource/sdk/).
- Baggage is the canonical runtime mechanism, but it requires the
  emitting SDK to publish baggage and a Baggage Span Processor to
  copy entries onto records —
  [Baggage concept](https://opentelemetry.io/docs/concepts/signals/baggage/).
  Claude Code's OTEL pipeline is closed to us; we can't inject either.

Every OTEL record Claude Code emits carries `session.id`. That's the
join key we have, and it's the one this design uses.

## Architecture

```
┌─────────────┐  OTLP/HTTP   ┌──────────────────────────────────────────┐
│ claude (CLI)│ ───────────► │  Custom OTel Collector (built via ocb)   │
│ session A   │  :4318       │                                          │
└─────┬───────┘              │  otlpreceiver                            │
      │                      │      │                                   │
      │ /enrich preprocess   │      ▼                                   │
      │                      │  attributesprocessor / batchprocessor    │
      │  POST control API    │      │                                   │
      ├─────────────────────►│      ▼                                   │
      │     :13133           │  ★ enrichmentprocessor (custom Go)       │
      │                      │      │ reads per-session map from        │
      │                      │      │ ★ enrichmentctlextension (custom) │
      │                      │      ▼                                   │
      │                      │  fileexporter  (default)                 │
      │                      │  otlphttpexporter (opt-in via config)    │
      │                      └─────────────┬─────────────┬──────────────┘
      │                                    ▼             ▼
      │                            ./output/*.jsonl   any OTLP backend
      │                                    (Honeycomb, Tempo, Datadog, …)
      │
      │ deterministic helpers (parsing, validation, scanning,
      │ slug normalisation, config probing, git-status parsing)
      │
      ▼
┌─────────────────────────────────────────────────────────────────┐
│  .NET 10 deterministic-helpers sidecar  (minimal API on :5050)    │
│  ◆ /helpers/plans/next-name        scan The-OTEL-Plan*.md        │
│  ◆ /helpers/topics/slugify         "fix the foo" → "fix-the-foo" │
│  ◆ /helpers/enrichments/validate   key+value rule check          │
│  ◆ /helpers/git/status             structured git state          │
│  ◆ /helpers/config/merge           merge OTEL env into settings  │
│  ◆ /openapi.json   /swagger        contract documentation        │
└─────────────────────────────────────────────────────────────────┘
```

★ = the only Go we write — listed in the OCB manifest alongside the
stock components.
◆ = the only .NET we write — every endpoint deterministic, OpenAPI-
documented.

### What's standard, what we wrote

| Concern | Component | Source |
|---|---|---|
| Receive OTLP from Claude Code | `otlpreceiver` | [stock, core](https://github.com/open-telemetry/opentelemetry-collector/tree/main/receiver/otlpreceiver) |
| Batch records | `batchprocessor` | [stock, core](https://github.com/open-telemetry/opentelemetry-collector/tree/main/processor/batchprocessor) |
| Static attributes (non-runtime) | `attributesprocessor` | [stock, contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/processor/attributesprocessor/README.md) |
| **Per-session runtime enrichment** | **`enrichmentprocessor` (ours)** | custom Go module |
| **HTTP control API + shared state** | **`enrichmentctlextension` (ours)** | custom Go module |
| Local file output (OTLP/JSON) | `fileexporter` | [stock, contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/exporter/fileexporter/README.md) |
| Forward to OTLP backend | `otlphttpexporter` | [stock, core](https://github.com/open-telemetry/opentelemetry-collector/tree/main/exporter/otlphttpexporter) |
| Health check | `healthcheckextension` | [stock, contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/extension/healthcheckextension) |
| Build the binary | `ocb` | [OpenTelemetry Collector Builder](https://github.com/open-telemetry/opentelemetry-collector/tree/main/cmd/builder) |
| **Deterministic operations skills need** | **`HelpersSidecar` (ours, .NET 10)** | custom .NET project, OpenAPI-documented |

Two Go modules + one small .NET project. Everything else is
configuration in standard `config.yaml`.

## How a developer uses it

Three skills in the demo. `/otel` and `/enrich` are user-only. The
worked example `/weather` is open to model invocation. A fourth
verb on `/otel`, `extend`, is what the maintainer types to safely
evolve the project.

```text
# /otel — bootstrap, master switch, and self-extension
/otel                              # FIRST RUN: merges OTEL env vars into
                                   #   .claude/settings.json, starts the bundled
                                   #   collector binary in the background, and
                                   #   reports ready. Idempotent: re-running just
                                   #   reports status. Always finishes by
                                   #   printing the full command list.
/otel on                           # this session's telemetry: enabled (default)
/otel off                          # this session's telemetry: paused. The
                                   #   collector silently drops batches whose
                                   #   session.id has collection disabled.
/otel status                       # show: is the collector running? is this
                                   #   session enabled? what's currently
                                   #   enriched onto this session?
/otel restart                      # restart the collector binary (config edits)
/otel help                         # print the bundled HELP.md
/otel set <key>:<value>            # add or update a PERSISTENT enrichment that
                                   #   applies to every session, every restart.
                                   #   Backed by ./persistent-enrichments.json.
/otel unset <key>                  # remove one persistent enrichment
/otel config                       # show all persistent enrichments
/otel config clear                 # wipe all persistent enrichments (confirms)
/otel extend [<topic>]             # delegates to the /otel-extend skill via skill
                                   #   chaining (see "Self-extension"). The user
                                   #   types this on /otel; Claude invokes
                                   #   /otel-extend through the Skill tool.

# /otel-extend — separate skill, hidden from the / menu (user-invocable: false).
# Reachable in two ways:
#   - chained from /otel extend (canonical path)
#   - direct invocation by typing /otel-extend <topic> (escape hatch)
# Performs the safe self-modification flow with git-tracked phases:
#   plan → implement → build → test, with revert callable at any phase.

# /enrich — manage per-session attributes
/enrich ticket.id PROJ-1234        # add or update an attribute
/enrich feature   payment-rewrite  # add another
/enrich --show                     # list current attributes for this session
/enrich --remove feature           # drop one
/enrich --clear                    # drop all attributes for this session

# /weather — example skill (Claude can invoke this one)
/weather                           # current weather, IP-located
/weather London                    # current weather for a named place
```

Two concurrent Claude sessions hold completely independent
**per-session** enrichment sets *and* collection state. `/otel off`
in session A does not affect session B; same for `/enrich`. The
`enrichmentctlextension` keys both flags per `session.id` and the
`enrichmentprocessor` reads them at the top of every batch.

`/otel off` does not clear enrichments — they're held in case the
session is turned back on. `/otel on` resumes collection with the
existing enrichment set intact.

### Two enrichment scopes: persistent vs. per-session

| Scope | Set by | Lifetime | Stored where | Applies to |
|---|---|---|---|---|
| **Persistent** | `/otel set k:v` | survives sessions and collector restarts | `./persistent-enrichments.json` | every record from every session |
| **Per-session** | `/enrich k v` | until the session ends or `/enrich --clear` | in-memory only | records carrying that `session.id` |

The processor stamps records in this order:

1. Apply the persistent map (global).
2. Apply the session map (overwrites if the same key appears).

So per-session is more specific and wins on conflict. Common pattern:
`/otel set team:platform` once at the start of a project (it applies
forever); `/enrich ticket.id PROJ-1234` per work-item.

`./persistent-enrichments.json` is a simple JSON object —
`{"team": "platform", "env": "production"}`. The
`enrichmentctlextension` loads it at startup and watches it for
changes; `/otel set` mutations write through the same file so the
file is always the source of truth. Treat it as project-scoped (in
the repo, but typically gitignored — values may include
team/env labels that aren't secrets but aren't useful to commit
either).

### Skill conventions (treated as rules)

The full upstream rules live in the cached conventions file:
`~/.claude/projects/C--Code-OTEL/memory/reference_skill_conventions.md`
(stale-while-revalidate, 7-day TTL — refresh in the background if
the file's mtime is older). That file is the authority for file
location, frontmatter fields, substitutions, shell preprocessing,
description budgets, and the top-level-skills-dir restart caveat.

This project's overlay on top of those rules:

- `/otel`, `/otel-extend`, `/enrich` →
  `disable-model-invocation: true` (user-only).
- `/otel-extend` → `user-invocable: false` (chained from `/otel
  extend` or typed directly; not in the `/` menu).
- `/weather` → all defaults (Claude may invoke).
- Helper scripts: Node modules; user input arrives via
  `process.argv[N]` (data, never code).
- Shell-exec: `$ARGUMENTS` always single-quoted in `!` lines.
- Deterministic work goes through the .NET helpers sidecar — no
  business logic in Node helpers (see "Skill security guardrails"
  rule 7).

**Skills are self-contained.** Every file a skill needs — helper
scripts, templates, phase playbooks, supporting reference material —
lives inside `.claude/skills/<name>/`. A skill never depends on a
project-root file existing or having particular content. Anything
the skill needs from the rest of the project comes via the documented
HTTP APIs of the two sidecars. This is what lets the skill be copied
into any project (or `~/.claude/skills/`) and work the same way.

The SKILL.md body is the entry point and is allowed to reference
sibling files (e.g. `[playbook.md](playbook.md)`) so Claude loads
them on demand. Per the conventions: `SKILL.md` stays under 500
lines; long reference material moves to siblings.

### Skill security guardrails

Skills are powerful — `!` preprocessing runs shell commands before
Claude sees anything, and a misconfigured `allowed-tools` list lets
those commands run without per-invocation prompts. The rules below
are tight by default; loosening any of them needs an explicit
justification in the change description.

**Cross-cutting rules (apply to every skill in this repo):**

1. **Helper scripts are Node modules invoked with single-quoted
   `'$ARGUMENTS'`.** Claude Code substitutes `$ARGUMENTS` verbatim
   into the command string *before* bash sees it. Double quotes let
   bash evaluate `$(…)` and backticks inside that string, which is an
   RCE primitive. Single quotes neutralise it. The helper takes
   user-supplied input via `process.argv[N]` — data, never code.
2. **`allowed-tools` is the narrowest pattern that works.** No
   `Bash(* *)`, no bare `Bash(curl *)`. Most skills use
   `Bash(node *)` because every helper is a Node process; the actual
   capability is whatever the Node script does (audited in source).
   `/otel extend` is the only intentional exception (see below).
3. **All listening ports bind `127.0.0.1`.** No external exposure
   without an explicit override flag and a startup banner.
4. **Helper file I/O is allow-listed by path.** A helper may only
   read or write paths it's documented to touch (table below). A
   helper never writes outside the project root, `~/.claude/`, or the
   `${CLAUDE_SKILL_DIR}` it lives in.
5. **Helper network egress is allow-listed by host.** Loopback for
   the collector control API, `wttr.in` for the example skill,
   nothing else.
6. **`disableSkillShellExecution` (managed setting) is honoured.**
   In restricted enterprise environments where it's set, skills
   degrade gracefully: the `!` line is replaced with the policy
   placeholder, the helper doesn't run, and the skill prints a clear
   message telling the user why.
7. **`/otel extend` is the only skill with broad capabilities,**
   gated behind explicit user confirmation per phase and tracked via
   git. Any other broad-capability skill needs the same gating.

**Per-skill capability matrix:**

| Skill / verb | Model invocation | `allowed-tools` | File reads | File writes | Network | Spawns | Justification |
|---|---|---|---|---|---|---|---|
| `/otel` (on / off / status / help) | **disabled** | `Bash(node *)` | `dist/<platform>/`, `.claude/settings.json`, `HELP.md` | `.claude/settings.json` only, with timestamped backup | `127.0.0.1:13133`, `127.0.0.1:13134` | none | runtime control only |
| `/otel` (no args == setup, restart) | **disabled** | `Bash(node *)` | as above | as above | as above | `dist/<platform>/claude-otel-collector` only — path resolved from `${CLAUDE_SKILL_DIR}/../../dist/<platform>/`; refuses if path resolution escapes the repo root | one-off bootstrap |
| `/otel` (set / unset / config) | **disabled** | `Bash(node *)` | none directly (collector reads/writes `./persistent-enrichments.json` on the helper's behalf) | none directly (same — file mutation happens server-side in the extension) | `127.0.0.1:13133` only | none | persistent-enrichment management — file I/O is centralised in the extension so the helper stays purely an HTTP client |
| `/otel extend` (verb) | **disabled** | `Bash(node *)` | none | none | none | none | thin dispatcher only — emits an `EXTEND_REQUESTED` marker into the prompt that instructs Claude to invoke `/otel-extend` via the `Skill` tool. Holds no broad capabilities itself. |
| `/otel-extend` (chained skill) | **enabled** (Claude can invoke via Skill tool); `user-invocable: false` (hidden from `/` menu) | `Bash(git *)`, `Bash(go *)`, `Bash(./bin/* *)`, `Skill`, `Read`, `Edit`, `Write`, `Glob`, `Grep` | repo-wide | repo-wide | none directly (downstream tools may call out) | `git`, `go`, `./dist/<platform>/ocb`, `./dist/<platform>/claude-otel-collector` | self-modification — broad by design, gated by per-phase user confirmation and git checkpoints. Reached via skill chain from `/otel` or by direct `/otel-extend` invocation. |
| `/enrich` | **disabled** | `Bash(node *)` | none | none | `127.0.0.1:13133` only | none | runtime control only |
| `/weather` | enabled (Claude may invoke) | `Bash(node *)` | none | none | `https://wttr.in` only (host hard-coded in helper) | none | example skill; demonstrates safe pattern |

**Per-skill input validation:**

- `/enrich`: key matches `^[a-z][a-z0-9_.\-]*$`, max 64 chars; value
  length ≤ 4096; helper warns (does not block) when value matches
  obvious-secret patterns
  (`^(AKIA|ghp_|gho_|ghu_|ghs_|ghr_|sk-|xoxb-)`).
- `/otel`: only the verbs `on`, `off`, `status`, `restart`, `help`,
  `setup`, `extend`, or empty (== `setup`) are accepted; anything
  else exits 1 with a usage message — preventing the helper from
  being abused as a general-purpose Node runner via stray arguments.
- `/otel extend`: the optional `<topic>` is a free-form string but
  it's only used as a label in plan filenames and commit messages
  (slugified to `[a-z0-9-]+`); never substituted into a shell
  command.
- `/weather`: helper passes the argument through
  `encodeURIComponent`; the host portion of the URL is a string
  literal in the helper, not derived from user input.

**Out-of-scope of skills *other than* `/otel extend` (forbidden):**

- Reading files outside the project tree or `~/.claude/`.
- Writing files outside `.claude/settings.json` (with backup) or the
  collector's `output/` directory.
- Any non-loopback network call other than to `wttr.in`.
- Spawning binaries other than the collector or `node`.
- Mutating `.gitignore`, source control state, or shell rc files.
- Loading or evaluating remote code.

## Self-extension via `/otel-extend` (chained from `/otel extend`)

This is a development-mode skill that lets the maintainer evolve the
project from inside Claude Code, with safety rails. It produces a new
plan file, makes the change, rebuilds, tests, and commits — each step
gated by user confirmation and tracked by git so any phase can be
reverted in isolation.

It's split out as a **separate skill** at
`.claude/skills/otel-extend/SKILL.md`. The `/otel` skill, when its
arg is `extend [<topic>]`, does no work itself beyond emitting an
`EXTEND_REQUESTED: topic="<topic>"` marker into the prompt. Claude
reads that marker and invokes the `/otel-extend` skill through the
`Skill` tool — that's the chain. This pattern is the cleanest
demonstration of skill chaining in this repo:

- The user keeps a single mental model (`/otel <verb>`).
- Capability surfaces stay isolated: `/otel` is restricted to a
  narrow `Bash(node *)` allowlist; the broad capabilities live only
  in `/otel-extend` and only kick in when the chain triggers.
- `/otel-extend` is `user-invocable: false`, so it's hidden from the
  `/` menu; the canonical reach is through `/otel extend`. Direct
  `/otel-extend <topic>` still works as an escape hatch.
- Skill chaining is achieved via the `Skill` tool — a documented
  Claude Code primitive, not a hidden mechanism.

### Phases

```
┌──────────────┐   ┌─────────┐   ┌───────────┐   ┌────────┐   ┌──────┐
│ 0 Pre-flight │──►│ 1 Plan  │──►│ 2 Imple-  │──►│ 3 Build│──►│ 4 Test│
│  git check   │   │ draft   │   │   ment    │   │ ocb    │   │ go   │
│              │   │ Plan-N  │   │ Edit/Write│   │        │   │ test │
└──────────────┘   └─────────┘   └───────────┘   └────────┘   └──────┘
       │                │                │             │           │
       └─ git init? ────┘                │             │           │
                                         │             │           │
                                  user confirms  user confirms   keep / revert
                                 each gate; each phase commits separately
```

**Phase 0 — Pre-flight (git safety):**

- Run `git status --short`.
- If **not a git repo:** ask the user
  *"This isn't a git repo. Without git, changes can't be safely
  reverted. Initialize a local git repo and commit the current state
  as a baseline?"*
  - **Yes:** `git init`, then `git add .` and
    `git commit -m "baseline: pre-extension snapshot"`. Proceed.
  - **No:** print a clear warning —
    *"Proceeding without git means any change is permanent. You will
    have no automatic revert path. Continue anyway?"* — and require a
    second explicit yes. If still yes, proceed but flag in every
    subsequent confirmation that revert is unavailable.
- If git repo with **uncommitted changes:** ask the user to commit
  or stash first; offer to do `git stash push -u -m "pre-extend"`
  for them. Don't proceed on a dirty tree.
- If clean: proceed.

**Phase 1 — Plan:**

- Glob `The-OTEL-Plan*.md` in the project root. Find the next
  available number `N` (existing files: `The-OTEL-Plan.md`,
  `The-OTEL-Plan-2.md`, … → next is `<max>+1` or `2` if only the
  base exists).
- If `<topic>` wasn't supplied, ask the user what they want to
  extend (free text) and then ask for a short kebab slug for the
  filename.
- Write `The-OTEL-Plan-<N>-<slug>.md` containing:
  - **Motivation** — what the user described
  - **Files affected** — exact paths
  - **Behavioural change** — before/after
  - **Test approach** — what an integration check would look like
  - **Rollback** — `git revert <commit-of-this-phase>` (filled in
    after commit)
- Commit: `git commit -am "plan: <topic> in The-OTEL-Plan-<N>-<slug>.md"`.
  Capture the SHA.
- Show the plan to the user. Ask:
  *"Implement this plan now? [yes / no / edit]"*
  - `edit` loops back into editing the plan file.
  - `no` exits cleanly; the plan file remains for later use.
  - `yes` proceeds to Phase 2.

**Phase 2 — Implement:**

- Apply each change from the plan via `Edit` / `Write`.
- After all changes:
  - Show a summary diff (`git diff --stat`).
  - Offer `show-full-diff` to dump it.
  - Ask:
    *"Commit these changes? [yes / no / show-full-diff / abort]"*
  - `yes` → `git add -A && git commit -m "feat(otel): <topic>"`.
    Capture SHA.
  - `abort` → `git checkout -- .` to revert working tree to the
    plan commit.

**Phase 3 — Build:**

- Run `./dist/<platform>/ocb --config=manifest.yaml` (or `ocb` on
  PATH).
- On success: stage updated binaries; ask
  *"Commit the rebuilt binaries? [yes / no]"*. On yes,
  `git commit -m "chore: rebuild collector for <topic>"`.
- On failure: surface error. Offer to fix-and-rebuild (loop back to
  Phase 2) or revert (Phase 5).

**Phase 4 — Test:**

- Ask *"Run integration tests? [yes / no / manual]"*.
- `yes` → `go test ./components/...` and any project-level
  integration script. Show pass/fail.
  - On all-pass: commit
    `git commit --allow-empty -m "test: green for <topic>"` and ask
    *"Keep changes? [keep / revert]"*. `keep` ends the flow.
  - On any failure: ask
    *"Tests failed. Revert all changes? [revert / diagnose / keep-with-failing-tests]"*.
- `manual` → print the test commands the user can run themselves and
  pause; resume on their say-so.

**Phase 5 — Revert (callable at any phase, including after the
flow):**

- The maintainer types `/otel extend revert` (or the helper offers it
  inline).
- The helper finds the most recent extend-flow commits via `git log`
  for messages prefixed `plan: `, `feat(otel): `, `chore: rebuild`,
  or `test: `.
- Shows them; asks the user how far back to revert.
- Performs `git revert <range>` (preferred — keeps history) or
  `git reset --hard <baseline>` (only if the user explicitly asks).

### Why each step is its own commit

So that any single phase can be reverted without disturbing the
others. If the build commit broke something but the implementation
was correct, `git revert <build-sha>` is enough. If the
implementation itself was wrong, `git revert <impl-sha>` doesn't
strand the plan file — the plan stays as a record.

### What `/otel extend` will not do

- Force-push, `git push --force`, or any push at all without explicit
  user direction.
- Modify `.gitignore` outside what the plan calls out.
- Delete files without staging the deletion as part of a commit the
  user has approved.
- Touch the user's git config or signing settings.
- Operate on a dirty working tree (refused in Phase 0).

### Lifecycle of an invocation

Both `/otel` (run-control) and `/enrich` use the same pattern: the
SKILL.md body runs a Node helper via the `` !`<command>` ``
preprocessing syntax so the work happens *before* the prompt reaches
Claude. `/otel extend` and `/weather` likewise dispatch through Node
helpers but with different capability surfaces. All helpers receive
`${CLAUDE_SESSION_ID}` and `${CLAUDE_SKILL_DIR}` as substitution
variables provided by Claude Code —
[slash command / skill docs](https://code.claude.com/docs/en/slash-commands).

**`/enrich <args>`**

1. Skill renders; helper runs:
   `node ${CLAUDE_SKILL_DIR}/scripts/enrich.mjs ${CLAUDE_SESSION_ID} <args>`.
2. Helper POSTs to
   `http://127.0.0.1:13133/sessions/<session_id>/enrichments` and
   prints a one-line confirmation.
3. The control extension updates its in-memory state. The very next
   OTLP batch from the same `session.id` is enriched.

**`/otel` (no args, first run)**

1. Skill renders; helper runs:
   `node ${CLAUDE_SKILL_DIR}/scripts/otel.mjs ${CLAUDE_SESSION_ID} setup`.
2. Helper:
   - Detects platform, locates the pre-built binary at
     `dist/<platform>/claude-otel-collector(.exe)` (shipped with the
     repo).
   - Merges the OTEL env block into `.claude/settings.json`,
     backing up the prior file with a timestamp.
   - Probes `127.0.0.1:13134/health`. If down, starts the binary as
     a detached background process (`spawn` with `detached: true,
     stdio: 'ignore'` on Node) using the bundled `config.yaml`.
   - Probes `/health` again until 200 (with a 10s timeout) and on
     success prints **a one-line ready banner followed by the full
     command list** — exactly the same list that appears in the
     README's "Commands" section so users see one unified reference
     no matter which surface they encounter first.
3. Re-running `/otel` is idempotent: each step is a check before an
   action. The command list still prints at the end so a returning
   user always has the cheat sheet to hand.

The single source of the command list lives in
`.claude/skills/otel/HELP.md`. The README's "Commands" section
embeds it verbatim (CI checks the two are in sync); `/otel help`
prints the same file. One list, three surfaces.

**`/otel on | off | status | restart`**

Helper POSTs to (or GETs from) the control extension's
`/sessions/<session_id>/collection` and `/sessions/<session_id>` (or
`/control/restart` for restart) endpoints. One line of stdout per
action.

**`/otel extend [<topic>]` → chains to `/otel-extend`**

1. The `/otel` skill renders. Its helper (`otel.mjs`) sees the
   `extend` verb and emits the chain marker into the prompt —
   roughly:

   ```
   EXTEND_REQUESTED: topic="<topic>"
   To proceed, invoke the `otel-extend` skill via the Skill tool with that topic as arguments.
   ```

   No broad-capability work happens in `/otel` itself.

2. Claude reads the marker and calls
   `Skill({ skill: "otel-extend", args: "<topic>" })`. The Skill
   tool is on `/otel`'s `allowed-tools` for this purpose only.

3. `/otel-extend`'s SKILL.md renders. Its helper
   (`otel-extend.mjs`) does the deterministic gathering work
   (`git status`, scanning plan files, suggesting the next plan
   number) and emits a richly-prompted playbook into the prompt.
   Claude conducts the conversation with the user across subsequent
   turns, calling `Edit`, `Write`, and `Bash` as the user confirms
   each gate. Every commit ends with the SHA echoed back to the user
   so revert is one command away.

A user who types `/otel-extend <topic>` directly skips step 1 and
lands at step 3.

## .NET deterministic-helpers sidecar

Skill helpers must not ask Claude to do deterministic work. Anything
that has a single correct answer — parsing, validation, scanning,
slug normalisation, config probing, git-status parsing — is an HTTP
call to this sidecar instead. The Node helper builds a request,
the sidecar returns the answer, the Node helper prints it; Claude
reads the result and reasons over it.

### Why a separate sidecar (and not Node logic in each helper)

- **Single source of truth** for things every helper needs. The plan
  numbering rule lives in one C# method, not five Node copies.
- **OpenAPI contract.** Every endpoint is documented; new
  contributors discover what's available without reading source.
  Skills are checked against the spec.
- **Strong typing.** C# records + System.ComponentModel validation
  give compile-time guarantees on the contract.
- **One pre-warmed process** per workstation rather than a fresh
  Node startup per `!` invocation.
- **Consistent telemetry.** The sidecar emits its own OTEL traces
  (through the collector — recursive but contained), so we can see
  helper latency.

### Endpoints (v1)

All bind `127.0.0.1:5050`. OpenAPI at `/openapi.json`; Swagger UI at
`/swagger`.

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/healthz` | liveness, version, build commit |
| `GET` | `/openapi.json` | full OpenAPI 3.1 spec |
| `GET` | `/swagger` | Swagger UI (dev convenience) |
| `POST` | `/helpers/plans/next-name` | given the project root, return the next available `The-OTEL-Plan-<N>-<slug>.md` filename |
| `POST` | `/helpers/topics/slugify` | "fix the foo bar" → "fix-the-foo-bar" |
| `POST` | `/helpers/enrichments/validate` | key+value rule check (key regex, value length, secret-pattern warnings) |
| `GET`  | `/helpers/git/status` | structured git state: clean/dirty, branch, ahead/behind, last-commit SHA. Refuses if not a git repo. |
| `POST` | `/helpers/config/merge` | merge an OTEL env block into a `.claude/settings.json`, returning the merged JSON plus a backup-suggested path |
| `POST` | `/helpers/binary/locate` | given platform, resolve the absolute path to `dist/<platform>/claude-otel-collector(.exe)`, refuse if path resolution escapes the repo root |

Each endpoint takes a JSON body, returns JSON. No streaming, no
auth, no TLS — localhost only.

### What the sidecar will NOT do

- Run binaries (helpers do that directly via Node `child_process`).
- Touch the file system outside the inputs/outputs documented per
  endpoint.
- Make external network calls.
- Hold any session state — the OTel Collector's
  `enrichmentctlextension` owns that.

### Running and lifecycle

- Started by `/otel` (no-args) alongside the OTel Collector.
  `/otel`'s helper probes both `:13134/health` (collector) and
  `:5050/healthz` (this sidecar) before reporting ready.
- Restarted by `/otel restart`.
- Stopped when Claude Code shuts down — both processes are detached
  but tied to the workstation session via standard process group
  semantics.

### Project structure (.NET)

```
src/HelpersSidecar/
├── HelpersSidecar.csproj         # SDK 10.0 (LTS), AOT-friendly
├── Program.cs                    # minimal API + OpenAPI registration
├── appsettings.json              # port, paths
├── Domain/                       # value objects + aggregates (DDD)
│   ├── PlanFileName.cs
│   ├── TopicSlug.cs
│   ├── EnrichmentKey.cs
│   ├── EnrichmentValue.cs
│   ├── PlatformIdentifier.cs
│   └── GitState.cs
├── Application/                  # use cases — one per endpoint
│   ├── NextPlanFileName/
│   ├── SlugifyTopic/
│   ├── ValidateEnrichment/
│   ├── ReadGitStatus/
│   ├── MergeConfig/
│   └── LocateBinary/
├── Infrastructure/               # I/O — file system, process spawn
│   ├── FileSystem.cs
│   └── GitInvoker.cs
└── Endpoints/                    # one minimal-API handler per use case
```

Each use case has unit tests in `tests/HelpersSidecar.Tests/`.

## Engineering practices (TDD + DDD)

Apply where applicable. Configuration files (OCB manifest, Collector
`config.yaml`) and skill SKILL.md don't have business rules in the
DDD sense; the Go custom components, the .NET helpers sidecar, and
any non-trivial helper logic do.

### Domain-Driven Design

**Bounded contexts (with their own ubiquitous language):**

| Context | Owns | Lives in |
|---|---|---|
| Telemetry pipeline | OTLP receive/transform/export | the OCB-built collector binary |
| Session attribution | the per-session enrichment + collection-enabled state | `enrichmentctlextension` Go module |
| Skill operations | deterministic helpers — plans, slugs, validation, git, config, paths | `src/HelpersSidecar/` .NET project |

The three contexts communicate only through documented HTTP
contracts (OTLP, the control API, the OpenAPI spec). No shared
in-process state across contexts.

**Ubiquitous language (a partial glossary; extend as new terms enter
the design):**

- *Session* — one Claude Code process invocation, identified by
  `session.id`. The unit of attribution.
- *Enrichment* — a `{key, value}` pair attached to a session's
  records.
- *Binding* — the act of associating an enrichment with a session.
- *Topic* — a free-form label for an extension flow; slugified for
  filenames and commit messages.
- *Phase* — one stage in the `/otel-extend` flow: plan / implement /
  build / test.
- *Collection* — telemetry pickup itself, toggleable per session by
  `/otel on|off`.

Use these terms exactly. Don't invent synonyms.

**Value objects** (each in `Domain/` of the .NET sidecar; equivalent
in the Go components):

- `SessionId` — a non-empty string, validated UUID-ish format.
- `EnrichmentKey` — string matching `^[a-z][a-z0-9_.\-]*$`, length
  ≤ 64.
- `EnrichmentValue` — string of length ≤ 4096.
- `TopicSlug` — lowercase alphanumeric + hyphens, length ≤ 64.
- `PlanFileName` — `The-OTEL-Plan(-N(-<slug>))?.md`.
- `PlatformIdentifier` — one of `windows-amd64 / darwin-arm64 /
  darwin-amd64 / linux-amd64 / linux-arm64`.
- `GitState` — clean / dirty / not-a-repo, with branch and last SHA.

Constructors validate; once a value object exists, its invariant
holds. Endpoint code receives parsed value objects, not strings.

**Aggregates:**

- `SessionEnrichments` — owns the map of `EnrichmentKey →
  EnrichmentValue` for one `SessionId`, plus the
  `collection_enabled` flag.
- `ExtensionFlow` — owns the state of one `/otel-extend` run (which
  phase, which commits, which plan file), serialised between turns
  via the existing git history.

Aggregates enforce invariants at their boundary; nothing else
mutates their state.

### Test-Driven Development with strict business-rule discipline

**The rule:** A test exists *if and only if* it proves a documented
business rule. Every business rule has at least one test that fails
when the rule is broken. CI rejects the PR otherwise.

**Concretely:**

1. **All business rules live in `docs/business-rules.md`**, keyed
   with stable IDs in the form `BR-<AREA>-<NN>`. Areas: `ENRICH`,
   `OTEL`, `EXTEND`, `SKILL`, `HELPERS`, `SECURITY`. Example:

   ```markdown
   ## BR-ENRICH-001 — Enrichment key syntax

   An enrichment key MUST match `^[a-z][a-z0-9_.\-]*$` and be
   ≤ 64 characters. Keys violating this rule are rejected with HTTP
   400 from `/sessions/{id}/enrichments`.

   *Reason: prevents OTLP attribute-namespace collisions and shell
   metacharacter risk.*
   ```

2. **Every test names its rule.** Test naming convention (xUnit,
   Go test, etc.):

   ```csharp
   [Fact(DisplayName = "BR-ENRICH-001 — invalid key chars are rejected")]
   public void EnrichmentKey_RejectsInvalidCharacters() { … }
   ```

   ```go
   func TestEnrichmentProcessor_BR_ENRICH_004_DropsBatchesWhenCollectionDisabled(t *testing.T) { … }
   ```

3. **No test without a BR.** If you can't name the rule, the test
   doesn't go in. Either document the rule first or delete the
   test.

4. **No BR without a test.** A CI step parses
   `docs/business-rules.md` for IDs and parses test output for
   `BR-` prefixes; the diff between the two sets must be empty. We
   ship a `make verify-rules` (or equivalent) target that runs the
   check locally.

5. **Red-green-refactor:** any new BR is added with a failing test
   first, then the implementation makes it pass. The commit history
   shows the cycle.

6. **Bug fixes follow the same rule.** A bug means a missing or
   wrong rule. Add or amend the BR, write the test that would have
   caught it, then fix.

**What is *not* a business rule:**

- Implementation choices (e.g. "we use `ConcurrentDictionary`") —
  refactor freely without changing tests.
- Pure plumbing (e.g. "endpoint X returns 200 OK on success") unless
  the success contract itself is a documented rule.
- Cosmetic or formatting choices.

**Coverage targets:**

- 100% of business rules have at least one passing test.
- Code coverage as a metric is informational, not a gate. A method
  with no business rule attached doesn't need a test.
- Integration tests count toward BR coverage if their assertion
  proves the rule end-to-end.

### Bootstrap business-rule list (will live in `docs/business-rules.md`)

The starting set, derived from this design doc:

| ID | Rule |
|---|---|
| `BR-ENRICH-001` | Enrichment key syntax `^[a-z][a-z0-9_.\-]*$`, len ≤ 64. |
| `BR-ENRICH-002` | Enrichment value length ≤ 4096; longer rejected with 400. |
| `BR-ENRICH-003` | Helper warns (does not block) when value matches an obvious-secret pattern. |
| `BR-ENRICH-004` | When a session's collection is disabled, the collector MUST drop OTLP batches whose `session.id` matches that session. Records from other sessions in the same batch pass through. |
| `BR-ENRICH-005` | Mid-session enrichment changes take effect at the next OTLP flush (no retroactive rewriting). |
| `BR-ENRICH-006` | Two concurrent sessions never share enrichment state — distinct `session.id`s are isolated. |
| `BR-ENRICH-007` | Persistent enrichments (set via `/otel set`) apply to every record from every session and survive collector restarts. |
| `BR-ENRICH-008` | Stamping order is persistent-first, per-session-second; per-session value overrides if the same key exists in both. |
| `BR-ENRICH-009` | `./persistent-enrichments.json` is the single source of truth for persistent enrichments. The extension loads it at startup; `/otel set` and `/otel unset` write through it; the in-memory copy is rebuilt from disk on FS change. |
| `BR-ENRICH-010` | Persistent enrichment keys and values follow the same syntax rules as per-session ones (`BR-ENRICH-001`, `BR-ENRICH-002`). The same secret-pattern warning applies. |
| `BR-ENRICH-011` | `/otel config clear` requires explicit user confirmation before wiping `./persistent-enrichments.json`. |
| `BR-OTEL-001`   | The OTel Collector binds `127.0.0.1` by default. Binding non-loopback requires explicit override + startup banner. |
| `BR-OTEL-002`   | `/otel off` does not clear the session's enrichment map; `/otel on` resumes with the existing set intact. |
| `BR-OTEL-003`   | `/otel` (no args) is idempotent: reruns are checks, not destructive actions. |
| `BR-OTEL-004`   | First-run setup backs up `.claude/settings.json` to `.claude/settings.json.bak.<timestamp>` before merging. |
| `BR-EXTEND-001` | `/otel-extend` refuses to start on a dirty git working tree. |
| `BR-EXTEND-002` | Each phase commits separately with the documented prefix (`plan: `, `feat(otel): `, `chore: `, `test: `). |
| `BR-EXTEND-003` | If no git repo exists, `/otel-extend` offers `git init` + baseline commit; refuses to proceed without an explicit second confirmation if declined. |
| `BR-EXTEND-004` | Plan files are numbered consecutively from the highest existing `The-OTEL-Plan-<N>(-<slug>)?.md`; gaps are not skipped. |
| `BR-EXTEND-005` | Topic slug normalisation: lowercase, alphanumeric + hyphens only, length ≤ 64, leading/trailing hyphens trimmed. |
| `BR-SKILL-001`  | Skill helpers single-quote `$ARGUMENTS` in `!` shell exec; double quotes are forbidden in this position. |
| `BR-SKILL-002`  | Skills with side effects set `disable-model-invocation: true`. |
| `BR-SKILL-003`  | Skills reachable only via chaining set `user-invocable: false`. |
| `BR-SKILL-004`  | Helper file I/O is allow-listed by path per skill; writes outside the documented set are forbidden. |
| `BR-SKILL-005`  | Helper network egress is allow-listed by host per skill; egress outside the documented set is forbidden. |
| `BR-SKILL-006`  | No AI for deterministic work — deterministic operations must call the .NET helpers sidecar, not embed logic in Node. |
| `BR-HELPERS-001` | Every endpoint on the helpers sidecar appears in the OpenAPI spec at `/openapi.json`. |
| `BR-HELPERS-002` | Helpers sidecar binds `127.0.0.1` only; refuses to bind otherwise without an explicit flag. |
| `BR-HELPERS-003` | `binary/locate` refuses paths that resolve outside the repo root. |
| `BR-SECURITY-001` | Skills must not load or evaluate remote code. |
| `BR-SECURITY-002` | `disableSkillShellExecution` (managed setting) is honoured: helper does not run; skill prints a clear policy message. |

This list grows as the project grows. Adding a feature without
adding a BR is a smell.

## Configuration

Standard OTel Collector `config.yaml`. The default ships with file
output only; adding `otlphttp` to the exporter list of any pipeline
is enough to also forward.

```yaml
receivers:
  otlp:
    protocols:
      http:
        endpoint: 127.0.0.1:4318

extensions:
  health_check:
    endpoint: 127.0.0.1:13134
  enrichmentctl:
    endpoint: 127.0.0.1:13133  # Node helper POSTs here

processors:
  enrichment:
    # references the extension above; nothing user-facing here
  batch:
    timeout: 200ms

exporters:
  file:
    path: ./output/telemetry.jsonl
    format: json
    rotation:
      max_megabytes: 100
      max_days: 7
      max_backups: 10
  otlphttp:                         # opt-in: configure and add to pipelines below
    endpoint: https://api.honeycomb.io
    headers:
      x-honeycomb-team: ${env:HONEYCOMB_API_KEY}

service:
  extensions: [health_check, enrichmentctl]
  pipelines:
    traces:
      receivers:  [otlp]
      processors: [enrichment, batch]
      exporters:  [file]                         # add otlphttp to also forward
    logs:
      receivers:  [otlp]
      processors: [enrichment, batch]
      exporters:  [file]
    metrics:
      receivers:  [otlp]
      processors: [enrichment, batch]
      exporters:  [file]
```

Switching from "local file only" to "local file + Honeycomb" is
adding `otlphttp` to three pipelines and setting the env var. No
rebuild.

## Standards mapping

OpenTelemetry's documented enrichment patterns and where this design
sits relative to each:

| Mechanism | Mutability | Source |
|---|---|---|
| `OTEL_RESOURCE_ATTRIBUTES` / programmatic Resource | frozen at SDK init | [Resource SDK spec](https://opentelemetry.io/docs/specs/otel/resource/sdk/) |
| Resource detectors (auto-populate resource attrs) | frozen at SDK init | [Resource SDK spec](https://opentelemetry.io/docs/specs/otel/resource/sdk/) |
| Baggage + Baggage Span Processor | runtime, per-request | [Baggage concept](https://opentelemetry.io/docs/concepts/signals/baggage/) |
| Per-span attributes in instrumentation | per-span | OTEL API spec |
| Collector `attributesprocessor` (`insert`/`update`/`upsert`/`delete`/`hash`/`extract`/`convert`) | static YAML | [README](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/processor/attributesprocessor/README.md) |
| Collector `transformprocessor` (OTTL) | static YAML | [README](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/processor/transformprocessor/README.md) |
| Collector `resourceprocessor` | static YAML | [Transforming telemetry](https://opentelemetry.io/docs/collector/transforming-telemetry/) |
| Collector `resourcedetectionprocessor` / `k8sattributesprocessor` | auto | [Transforming telemetry](https://opentelemetry.io/docs/collector/transforming-telemetry/) |
| Collector `fileexporter` (the local-file output) | n/a | [README](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/exporter/fileexporter/README.md) |
| Collector `otlphttpexporter` (the forward-to-backend exporter) | n/a | [source](https://github.com/open-telemetry/opentelemetry-collector/tree/main/exporter/otlphttpexporter) |
| Collector Builder (ocb) | n/a | [README](https://github.com/open-telemetry/opentelemetry-collector/tree/main/cmd/builder) |
| Claude Code OTEL emission | n/a | [Claude Code monitoring](https://code.claude.com/docs/en/monitoring-usage) |
| Claude Code skills / slash commands | n/a | [Slash command / skill docs](https://code.claude.com/docs/en/slash-commands) |

OpenTelemetry's official guidance:

> "The OpenTelemetry Collector is a convenient place to transform data before sending it to a vendor or other systems."
> ([Transforming telemetry](https://opentelemetry.io/docs/collector/transforming-telemetry/))

Our distribution is a Collector. The `enrichmentprocessor` is a
runtime-mutable cousin of `attributesprocessor` in `upsert` mode; the
runtime mutability isn't a documented OTEL pattern (Baggage would be,
if Claude Code's SDK were open to us). Everything else is configured
the same way as any vanilla Collector.

## Threat model

- **Trust boundary:** the local user. Anyone with shell access to the
  workstation can hit the control API and read `output/`. Same model
  as any local Collector.
- **Network exposure:** all listeners bind `127.0.0.1`. Binding to a
  non-loopback address requires an explicit override flag and prints
  a banner on startup.
- **Secret leakage:** values typed into `/enrich` are written to
  `output/` verbatim. The README warns against passing credentials or
  PII as enrichment values.
- **Self-modification:** `/otel extend` has wide capability. It is
  user-only (`disable-model-invocation: true`), gated by
  per-phase confirmation, and checkpointed via git — so any change
  has a one-command revert.
- **Resource limits:** `enrichmentprocessor` rejects values longer
  than `max_attribute_value_length` (default 4096) with HTTP 400 from
  the control API.

## Operations

- **Logs:** the Collector emits structured logs to stdout via its
  built-in `service.telemetry.logs` config. Set log level there.
- **Health:** `GET http://127.0.0.1:13134/` (standard
  `healthcheckextension`).
- **Diagnostics:** the control extension exposes
  `GET /sessions/{id}/enrichments` and `GET /sessions` (list of
  active sessions with non-empty maps).
- **Metrics about itself:** `service.telemetry.metrics` from the
  Collector emits its own metrics on a separate port; the
  `enrichmentprocessor` adds counters
  (`enrichment.records_total`, `enrichment.records_enriched_total`,
  `enrichment.records_missing_session_id_total`,
  `enrichment.control_op_total{op=set|remove|clear}`).

## Verification (acceptance criteria)

| # | Action | Expected |
|---|---|---|
| 1 | Clone repo; open in Claude Code; type `/otel` | Settings merged, binary started, healthz returns 200, command list printed |
| 2 | `/enrich ticket.id TEST-1`; ask Claude to read a file | New lines in `output/telemetry.jsonl` carry `ticket.id=TEST-1` on relevant records |
| 3 | `/enrich feature login`; another tool call | Subsequent records carry both attributes |
| 4 | `/enrich --show` | Helper prints both keys |
| 5 | `/enrich --remove feature`; another tool call | New records carry only `ticket.id` |
| 6 | `/enrich --clear`; another tool call | New records carry no user-added attributes |
| 7 | Open second `claude` session; `/enrich ticket.id TEST-9`; tool calls in each session | Filter `output/` by `session.id`: session A still tagged `TEST-1`, session B tagged `TEST-9`. **No crosstalk.** |
| 8 | Add `otlphttp` to the traces pipeline; `/otel restart`; tool call | Records appear in both `output/` and the configured backend |
| 9 | Kill collector mid-session; `/otel` again | Healthz back to 200; enrichments empty (in-memory only); user re-issues `/enrich` |
| 10 | `/weather` | Current weather lines printed; Claude summarises in one sentence |
| 11 | `/otel extend test-extension` in a non-git dir | Helper offers to `git init` and proceed; on yes, baseline commit created |
| 12 | `/otel extend test-extension` mid-flow → user says "no" at Phase 1 | Plan file remains in repo; no source changes; clean working tree |
| 13 | `/otel extend test-extension` full happy-path → tests fail at Phase 4 | User offered revert; choosing revert returns repo to baseline SHA |

CI runs steps 1–10 against synthetic OTLP traffic on Windows, macOS,
and Linux runners. Steps 11–13 run on the Linux runner only.

## Repository layout

The repo *is* the demo project — opening it in Claude Code and typing
`/otel` is the canonical happy path. Skills are committed under
`.claude/skills/` so they're already on disk before Claude starts.

```
C:\Code\OTEL\
├── README.md
├── The-OTEL-Plan.md                       # this document
├── DESIGN.md                              # symlink to The-OTEL-Plan.md (or duplicate)
├── LICENSE
├── manifest.yaml                          # OCB manifest pinning all components
├── config.yaml                            # default Collector config
├── persistent-enrichments.json            # /otel set state — JSON {key: value}; gitignored
├── dist/                                  # pre-built binaries shipped with releases
│   ├── windows-amd64/                     # named `dist/`, not `bin/`, to avoid
│   ├── darwin-arm64/                      #   colliding with the .NET project's
│   ├── darwin-amd64/                      #   own bin/ build output
│   ├── linux-amd64/
│   └── linux-arm64/
├── components/
│   ├── enrichmentprocessor/               # Go module — ~150 LOC
│   │   ├── go.mod
│   │   ├── factory.go
│   │   ├── config.go
│   │   ├── processor.go
│   │   └── *_test.go
│   └── enrichmentctlextension/            # Go module — ~150 LOC
│       ├── go.mod
│       ├── factory.go
│       ├── config.go
│       ├── extension.go                   # HTTP control API + shared state
│       └── *_test.go
├── src/
│   └── HelpersSidecar/                    # .NET 10 deterministic helpers
│       ├── HelpersSidecar.csproj
│       ├── Program.cs                     # minimal API + Swagger
│       ├── appsettings.json
│       ├── Domain/                        # DDD value objects + aggregates
│       ├── Application/                   # use cases (one per endpoint)
│       ├── Infrastructure/                # FS, Git, process invokers
│       ├── Endpoints/                     # minimal-API handlers
│       └── openapi.json                   # checked-in static copy
├── tests/
│   ├── HelpersSidecar.Tests/              # xUnit, TDD-driven
│   ├── enrichmentprocessor.Tests/         # Go test
│   └── integration/                       # spins both sidecars, drives e2e
├── .claude/
│   ├── settings.json                      # OTEL env vars (committed)
│   └── skills/
│       ├── otel/
│       │   ├── SKILL.md
│       │   ├── HELP.md                    # canonical command list (also embedded in README)
│       │   └── scripts/
│       │       └── otel.mjs               # on / off / status / restart / setup / help / extend-dispatch
│       ├── otel-extend/                   # chained skill, user-invocable: false
│       │   │                              # self-contained: every file lives
│       │   │                              # inside this directory
│       │   ├── SKILL.md                   # entry point; references the rest
│       │   ├── playbook.md                # the multi-phase flow Claude drives
│       │   ├── phases.md                  # detailed per-phase gates
│       │   ├── commit-prefixes.md         # canonical prefixes for each phase
│       │   ├── business-rules.md          # BR-EXTEND-* covered by this skill
│       │   ├── templates/
│       │   │   └── plan-template.md       # what Phase 1 fills in
│       │   └── scripts/
│       │       └── otel-extend.mjs        # the deterministic gathering helper
│       ├── enrich/
│       │   ├── SKILL.md
│       │   └── scripts/
│       │       └── enrich.mjs
│       └── weather/
│           ├── SKILL.md
│           └── scripts/
│               └── weather.mjs
├── docs/
│   ├── architecture.md
│   ├── operations.md
│   ├── threat-model.md
│   └── standards-mapping.md
└── tests/
    └── integration/                       # spins up the binary, sends OTLP, asserts file output
```

### Installing into a different project

Two paths. In both, copy the **four** skill directories
(`otel`, `otel-extend`, `enrich`, `weather`) — the chain breaks if
`/otel-extend` is missing.

1. **Personal scope (recommended):** copy the four skills from this
   repo to `~/.claude/skills/{otel,otel-extend,enrich,weather}` once.
   They become available in every Claude Code session you run.
   Restart Claude after the copy if `~/.claude/skills/` didn't exist
   before — per the convention, a brand-new top-level skills dir
   requires a restart to be watched.
2. **Project scope:** copy the skills to
   `<your-project>/.claude/skills/{otel,otel-extend,enrich,weather}`.
   Same restart caveat applies. `/otel` first-run will then merge
   OTEL env vars into that project's `.claude/settings.json`.

The collector binary (from `dist/<platform>/`) is launched by `/otel`
either way; it doesn't have to live inside the target project.

### Skill chaining (worked example: `/otel` → `/otel-extend`)

A pattern this repo demonstrates and that other skill authors can
copy. The shape:

```
┌──────────────┐  emits marker text   ┌──────────────────┐
│  /otel       │ ───────────────────► │ Claude (in turn) │
│  (verb=extend│                      │ sees marker,     │
│   dispatcher)│                      │ calls Skill tool │
└──────────────┘                      └────────┬─────────┘
                                               │
                                               ▼
                                  Skill({skill: "otel-extend", args: "<topic>"})
                                               │
                                               ▼
                                       ┌─────────────────┐
                                       │  /otel-extend   │
                                       │  (broad caps,   │
                                       │   gated by user │
                                       │   confirmation) │
                                       └─────────────────┘
```

**Why split rather than make `extend` a verb on `/otel`:**

- **Capability isolation.** `/otel`'s `allowed-tools` stays narrow
  (`Bash(node *)` plus `Skill` purely for chaining). The broad
  capabilities — `Bash(git *)`, `Edit`, `Write`, etc. — live only on
  `/otel-extend`. Auditing the security of `/otel` doesn't require
  re-checking the self-modification flow.
- **Single source for the dispatcher mark.** Adding new chained verbs
  to `/otel` (say a future `/otel migrate`) just means another marker
  type the helper emits — no new capability to negotiate on `/otel`.
- **Discoverability vs. footgun balance.** `/otel-extend` is hidden
  from the menu (`user-invocable: false`), so casual users discover
  only `/otel`. Power users who type `/otel-extend` directly get the
  same flow.
- **Demo value.** Other skill authors learn the chaining primitive
  by reading the source.

**The `Skill` tool primitive.** Claude Code documents `Skill({skill,
args})` for in-conversation skill invocation. The chained skill
loads its own SKILL.md content into the conversation as a fresh
message, runs its own `!` preprocessing, and Claude continues from
there.

**Sketches of the chained skill:**

`.claude/skills/otel-extend/SKILL.md`:

```markdown
---
name: otel-extend
description: Self-modification flow for the OTEL collector — drafts a plan, applies changes, rebuilds, tests, and commits each phase under git. Invoke only when /otel emits an EXTEND_REQUESTED marker, or when the user types /otel-extend directly.
argument-hint: [topic]
disable-model-invocation: false
user-invocable: false
allowed-tools: Bash(git *) Bash(go *) Bash(./bin/* *) Read Edit Write Glob Grep
---

!`node '${CLAUDE_SKILL_DIR}/scripts/otel-extend.mjs' '$ARGUMENTS'`

You now have:
- the current git state and clean/dirty status
- the next available plan filename
- the topic the user requested

Drive the multi-phase workflow described in The-OTEL-Plan.md
("Self-extension via /otel-extend"). Wait for explicit user
confirmation at each gate. Commit each phase separately with the
prefixes `plan: `, `feat(otel): `, `chore: `, `test: `. Echo every
SHA back to the user.
```

`.claude/skills/otel-extend/scripts/otel-extend.mjs` does only the
deterministic gathering work — `git status`, plan-file scan, topic
slug normalisation — and prints structured context for Claude to
act on. It does **not** make any change itself; Claude does, after
each user gate.

The dispatcher line in `.claude/skills/otel/SKILL.md`'s helper
(`otel.mjs`) for the `extend` verb is just:

```
console.log(`EXTEND_REQUESTED: topic="${topic}"`);
console.log(`Invoke the \`otel-extend\` skill via the Skill tool with topic="${topic}".`);
```

Claude reads those two lines and calls `Skill({skill: "otel-extend",
args: topic})`.

### Example test skill: `/weather`

A minimal skill that demonstrates the pattern end-users would copy to
build their own. Hits `wttr.in` — a free public weather endpoint that
falls back to IP-based geolocation when no location is supplied,
giving the "first site it can find" behaviour out of the box.

The naive form (`curl … "https://wttr.in/$ARGUMENTS?format=3"`) is
**unsafe**: Claude Code substitutes `$ARGUMENTS` verbatim into the
command before bash sees it, and any `$(…)` or backticks the user
(or Claude) types will be evaluated by bash inside the double quotes.
We route through a Node helper instead so user-supplied input is
treated as data (argv) not code.

`.claude/skills/weather/SKILL.md`:

```markdown
---
name: weather
description: Show the current weather. With no argument, uses IP-based location. With an argument like "London" or "94103", reports for that location.
argument-hint: [location]
allowed-tools: Bash(node *)
---

!`node '${CLAUDE_SKILL_DIR}/scripts/weather.mjs' '$ARGUMENTS'`

Briefly summarise the weather shown above in one short sentence.
```

`.claude/skills/weather/scripts/weather.mjs`:

```js
const arg = process.argv[2] ?? '';
const url = `https://wttr.in/${encodeURIComponent(arg)}?format=3`;
try {
  const r = await fetch(url, { signal: AbortSignal.timeout(5000) });
  console.log((await r.text()).trim());
} catch (e) {
  console.log(`weather lookup failed: ${e.message}`);
  process.exit(1);
}
```

## Out of scope (v1)

- gRPC OTLP receiver (HTTP/protobuf only). The stock `otlpreceiver`
  supports both; we just don't enable gRPC by default.
- TLS / auth on the receiver and control endpoints (localhost only).
- Persisting the per-session enrichment map across restarts. Optional
  flag `enrichmentctl.persistence.enabled` is a v2 add.
- Type-tagged enrichment values; v1 stores everything as strings.
- A built-in dashboard. `/sessions` returns JSON; that's enough.
- Closing the mid-prompt race window (an OTLP batch already in flight
  may arrive after a `/enrich` change and pick up the new value).
- Auto-pushing extension commits to a remote — `/otel extend` only
  ever creates local commits.

## Roadmap

- **Persistence** for the enrichment map (small JSON file, atomic
  write).
- **Effective-at semantics** so a binding only stamps records whose
  timestamp ≥ when it was set.
- **Type-tagged values:** `/enrich --int count 42`,
  `/enrich --bool enabled true`, etc.
- **Web UI** at `http://127.0.0.1:13133/`: active sessions, current
  maps, healthz at a glance.
- **gRPC** receiver and exporter parity.
- **Plugin distribution:** publish the OCB manifest as a Claude Code
  plugin so `claude plugin install` does the whole thing.
- **`/otel extend --remote`** that creates extension commits on a
  feature branch and offers to open a PR.

## References

- Resource SDK spec — https://opentelemetry.io/docs/specs/otel/resource/sdk/
- Baggage concept — https://opentelemetry.io/docs/concepts/signals/baggage/
- Transforming telemetry (Collector guidance) — https://opentelemetry.io/docs/collector/transforming-telemetry/
- `attributesprocessor` README — https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/processor/attributesprocessor/README.md
- `transformprocessor` README — https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/processor/transformprocessor/README.md
- `fileexporter` README — https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/exporter/fileexporter/README.md
- `otlphttpexporter` source — https://github.com/open-telemetry/opentelemetry-collector/tree/main/exporter/otlphttpexporter
- `otlpreceiver` source — https://github.com/open-telemetry/opentelemetry-collector/tree/main/receiver/otlpreceiver
- OpenTelemetry Collector Builder (ocb) — https://github.com/open-telemetry/opentelemetry-collector/tree/main/cmd/builder
- Claude Code monitoring (OTEL emission) — https://code.claude.com/docs/en/monitoring-usage
- Claude Code skills / slash commands — https://code.claude.com/docs/en/slash-commands
