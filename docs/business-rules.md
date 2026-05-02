# Business rules

This file is the **test target** for this project. Every test —
unit, integration, end-to-end — must name a rule from this file via
its display name (`BR-<AREA>-<NN>`). CI parses both this file and
test output and rejects the build if either side of this
biconditional is violated:

- A test exists ⇔ a documented business rule exists.
- A business rule has at least one passing test.

A test that doesn't name a BR is a smell: either document the rule
first, or delete the test. A BR without a test means we shipped
behaviour we can't verify — block the merge.

The **areas**:

- `ENRICH` — enrichment semantics (per-session and persistent).
- `OTEL` — collector lifecycle and runtime control.
- `EXTEND` — `/otel-extend` self-modification flow.
- `SKILL` — skill-author rules and helper safety.
- `HELPERS` — the .NET deterministic helpers sidecar.
- `SECURITY` — cross-cutting safety constraints.

New areas need a written justification in the PR that adds them.

---

## ENRICH

### BR-ENRICH-001 — Enrichment key syntax

An enrichment key MUST match `^[a-z][a-z0-9_.\-]*$` and be ≤ 64
characters. Keys violating this rule are rejected with HTTP 400
from `/sessions/{id}/enrichments`,
`/persistent-enrichments`, and
`/helpers/enrichments/validate`.

**Why:** prevents OTLP attribute-namespace collisions and shell
metacharacter risk in any downstream that interpolates the key.

### BR-ENRICH-002 — Enrichment value length cap

An enrichment value MUST be ≤ 4096 characters. Longer values are
rejected with HTTP 400 from the same endpoints as BR-ENRICH-001.

**Why:** OTLP attribute values are bounded; long values bloat
records and leak intent across batches.

### BR-ENRICH-003 — Obvious-secret-pattern warning

When an enrichment value matches an obvious-secret pattern
(`^(AKIA|ghp_|gho_|ghu_|ghs_|ghr_|sk-|xoxb-)`), the helper
**warns** but does NOT block the operation. The warning is
surfaced in the response body.

**Why:** these prefixes belong to AWS, GitHub, OpenAI, Slack
credentials. Telemetry is not a place to keep them.

### BR-ENRICH-004 — Collection-disabled drops batches by session.id

When a session's `collection_enabled` flag is `false`, the
collector MUST drop OTLP batches whose `session.id` matches that
session. Records from other sessions in the same batch (rare in
practice) pass through unchanged.

**Why:** `/otel off` must actually pause that session's telemetry,
without disturbing other concurrent sessions.

### BR-ENRICH-005 — Mid-session changes effective at next flush

Mid-session enrichment changes take effect at the next OTLP flush.
Records already in flight may arrive after the change and be
stamped with the new value. The collector does NOT rewrite records
retroactively.

**Why:** OTLP exporters are batched at the source; eventual
consistency at batch granularity is the documented contract.

### BR-ENRICH-006 — Per-session isolation

Two concurrent sessions never share enrichment state. Distinct
`session.id`s have distinct in-memory maps; a `/enrich` call on
one session has no effect on records carrying a different
`session.id`.

**Why:** the whole point of session-scoped enrichment is that
parallel work doesn't cross-contaminate.

### BR-ENRICH-007 — Persistent enrichments survive restarts

Persistent enrichments (set via `/otel set <key>:<value>`) apply
to every record from every session and survive collector restarts.

**Why:** the user's stable labels (team, env, cost-centre) shouldn't
need to be re-set after every reboot.

### BR-ENRICH-008 — Stamping order: persistent first, per-session wins

The processor stamps records in this order:

1. Apply the persistent map (global).
2. Apply the per-session map; if a key exists in both, the
   per-session value overwrites.

**Why:** per-session is more specific. A user's transient
`/enrich ticket.id PROJ-1234` should override a previously-set
persistent default for the same key.

### BR-ENRICH-009 — `persistent-enrichments.json` is the source of truth

The `enrichmentctlextension` loads `./persistent-enrichments.json`
at startup. `/otel set` and `/otel unset` write through it; the
in-memory map is rebuilt from disk on filesystem-change events.
The file is the single source of truth for persistent enrichments.

**Why:** disk-backed state survives crashes; an in-memory-only
copy could diverge from the file under concurrent edits.

### BR-ENRICH-010 — Persistent uses the same key/value rules

Persistent enrichment keys and values follow BR-ENRICH-001 and
BR-ENRICH-002. The secret-pattern warning of BR-ENRICH-003 also
applies.

**Why:** consistency between scopes; users learn one set of rules.

### BR-ENRICH-011 — `/otel config clear` requires confirmation

`/otel config clear` MUST prompt for explicit user confirmation
before wiping `persistent-enrichments.json`. Without confirmation,
the helper exits non-zero with a "clear refused — confirm with
--yes" message.

**Why:** wiping persistent enrichments is destructive and
unrecoverable without backup.

### BR-ENRICH-013 — `/otel get <key>` returns single value or 404

`/otel get <key>` reads one persistent enrichment by key. If the
key is set, returns 200 with the value as plain text. If the key
is unset, returns 404 with no body (and helper prints `(unset)`
to stdout).

**Why:** lets the user (and skills) check a single persistent
attribute without dumping the whole map.

### BR-ENRICH-014 — `/otel get` accepts multiple keys

`/otel get <key1> <key2> ...` (two or more keys) returns 200 with
a JSON array, one entry per requested key in the order requested:

```json
[
  { "key": "team",    "value": "platform", "exists": true },
  { "key": "missing", "value": null,       "exists": false }
]
```

The response is always 200 — missing keys are signalled by
`exists: false`, not by status code. The helper prints one line
per requested key in the user's terminal.

**Why:** scripts and skills often need to read several persistent
attributes at once; one round-trip beats N. Per-key existence is
explicit so callers don't conflate "missing" with "empty value".

### BR-ENRICH-012 — Concurrent reads must see a consistent snapshot

The processor reads enrichment state (per-session map, persistent
map, collection-enabled flags) on every OTLP batch, potentially
from many goroutines simultaneously. Writes from the HTTP control
API and from `persistent-enrichments.json` filesystem events
happen rarely. Each read MUST see a single consistent snapshot —
no partial-update visibility, no stale-with-new mixing.

The implementation uses atomic-pointer-swap to immutable maps:
writers build a new map and `CompareAndSwap` it into place;
readers do a single atomic load. Tests cover concurrent
read/write with `go test -race`.

**Why:** OTEL processors are called concurrently. A racy state
read could attribute records to the wrong ticket or drop the
wrong session's collection-enabled flag.

---

## OTEL

### BR-OTEL-001 — Default localhost binding

The OTel Collector and the helpers sidecar bind `127.0.0.1` by
default. Binding to a non-loopback address requires an explicit
override flag AND prints a banner on startup naming the bound
interface.

**Why:** localhost-only is the trust boundary documented in the
threat model. Public exposure must be a loud, deliberate choice.

### BR-OTEL-002 — `/otel off` preserves enrichments

`/otel off` does not clear the session's enrichment map; `/otel
on` resumes collection with the existing set intact.

**Why:** `off` is "pause", not "discard". Users toggle frequently
and shouldn't lose context each time.

### BR-OTEL-003 — `/otel` (no args) is idempotent

`/otel` with no arguments is the bootstrap-and-status command.
Repeated invocations check state and report; they do not
re-perform destructive actions (no extra `.bak` files, no
duplicate spawns).

**Why:** users will run it more than once. Each invocation should
be a check before any action.

### BR-OTEL-004 — Settings backup before merge

First-run setup MUST back up `.claude/settings.json` to
`.claude/settings.json.bak.<timestamp>` before merging the OTEL
env block.

**Why:** the user's existing settings might contain unrelated
configuration. Merging without a backup is destructive.

---

## EXTEND

### BR-EXTEND-001 — Refuse to start on a dirty working tree

`/otel-extend` Phase 0 MUST refuse to proceed if `git status`
reports uncommitted changes. The user is offered to commit or
stash before continuing.

**Why:** the flow's revert primitives assume a clean baseline.

### BR-EXTEND-002 — Per-phase commit prefixes

Each phase commits separately with a documented prefix:

- Phase 1: `plan: <topic> in The-OTEL-Plan-<N>(-<slug>)?.md`
- Phase 2: `feat(otel): <topic>`
- Phase 3: `chore: rebuild collector for <topic>`
- Phase 4: `test: green for <topic>` (or describes failure)

**Why:** phase isolation lets a single phase be reverted without
disturbing the others.

### BR-EXTEND-003 — Offer git init before proceeding without a repo

If `/otel-extend` is invoked outside a git repo, it offers to run
`git init` and create a baseline commit. If the user declines, it
warns and requires a second explicit confirmation before any
subsequent destructive action.

**Why:** without git, the flow has no revert path. Two confirmations
make the loss-of-safety choice deliberate.

### BR-EXTEND-004 — Plan numbering is consecutive

Plan files are named `The-OTEL-Plan(-<N>(-<slug>))?.md`. The next
available number is the maximum of existing N values plus 1, or 2
if only the base file `The-OTEL-Plan.md` exists. Gaps (e.g. 1, 3,
5) are not skipped — the next is still max+1.

**Why:** predictable numbering. No "should we fill the gap?" debate.

### BR-EXTEND-005 — Topic slug normalisation

A topic slug is produced from arbitrary user text by:

1. Lowercasing.
2. Replacing each run of non-alphanumeric characters with a single
   `-`.
3. Trimming leading and trailing `-`.
4. Truncating to 64 characters maximum.

Slug must match `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$` after
normalisation; if normalisation produces an empty string (e.g.
input was all symbols), the helper returns HTTP 400.

**Why:** slugs end up in filenames and commit messages. Predictable
output and safe characters only.

---

## SKILL

### BR-SKILL-001 — `$ARGUMENTS` single-quoted in `!` shell exec

Every `!`-prefixed shell-exec line in any SKILL.md in this repo
MUST single-quote `$ARGUMENTS`. Double quotes (or no quotes) are
forbidden in this position.

**Why:** Claude Code substitutes `$ARGUMENTS` verbatim before bash
sees the command. With double quotes, bash will evaluate `$(…)` and
backticks inside the substituted string — an RCE primitive.

### BR-SKILL-002 — User-only skills set `disable-model-invocation: true`

Any skill with side effects (state changes, file writes, network
calls beyond local sidecars) MUST set `disable-model-invocation:
true` so Claude cannot invoke it without explicit user action.

**Why:** prevents Claude from triggering destructive flows on its
own initiative.

### BR-SKILL-003 — Chain-only skills set `user-invocable: false`

A skill reachable only via chaining from another skill MUST set
`user-invocable: false` so it doesn't appear in the `/` menu.
Direct typing of `/<name>` still works as an escape hatch.

**Why:** capability isolation; the user's primary surface stays
narrow.

### BR-SKILL-004 — Helper file I/O is allow-listed by path

A skill helper may only read or write paths documented in The-
OTEL-Plan.md's capability matrix for that skill. Writes outside
the documented set are forbidden.

**Why:** prevents skill drift into a general-purpose file editor.

### BR-SKILL-005 — Helper network egress is allow-listed by host

A skill helper may only reach hosts documented in the capability
matrix for that skill. Egress outside the documented set is
forbidden.

**Why:** prevents skills from becoming exfiltration tools.

### BR-SKILL-006 — Deterministic work uses the .NET sidecar

Skill helpers must NOT ask the LLM to perform deterministic work.
Deterministic operations (parsing, validation, scanning, slug
normalisation, config probing, git-status parsing) MUST call the
.NET helpers sidecar.

**Why:** reproducibility, cost, speed, audit, and security — see
README's "Single sidecar for deterministic work — pros and cons".

---

## HELPERS

### BR-HELPERS-001 — Every endpoint appears in OpenAPI

Every endpoint exposed by the helpers sidecar MUST appear in the
OpenAPI spec at `/openapi.json` with a complete schema (parameters,
request body, response, status codes). Endpoints absent from the
spec MUST return 404.

**Why:** OpenAPI is the contract. Undocumented endpoints are
unaudited surface area.

### BR-HELPERS-002 — Sidecar binds 127.0.0.1 only by default

The helpers sidecar binds `127.0.0.1:5050` by default. Binding to
another address requires `Listener:AllowPublicBind=true` AND prints
a banner on startup. (Same pattern as BR-OTEL-001.)

**Why:** localhost trust boundary.

### BR-HELPERS-004 — `/healthz` returns structured liveness payload

`GET /healthz` MUST return HTTP 200 with a JSON body containing
`status: "ok"`, an integer `uptime_s` (seconds since process
start), and a non-empty `version` string. The endpoint is the
liveness probe `/otel`'s setup helper polls before declaring
ready.

**Why:** `/otel` (no args) probes this endpoint until 200 to
confirm the sidecar is live. Without a stable contract, the
bootstrap path is brittle.

### BR-HELPERS-003 — `binary/locate` refuses paths outside the repo

The `/helpers/binary/locate` endpoint MUST refuse any resolved path
that escapes the repo root (e.g. via symlink or `..` traversal).
A refused request returns HTTP 400 with a "path-escape" error
code.

**Why:** the endpoint is one of the few that can name an absolute
path the calling skill might `spawn`. Path confusion here is RCE.

---

## SECURITY

### BR-SECURITY-001 — No remote code execution from skills

Skills MUST NOT load or evaluate remote code. No `eval`, no
`require()` of a downloaded URL, no `curl … | bash`, no fetching
a script and running it.

**Why:** the supply chain stops at the repo's committed source.

### BR-SECURITY-003 — Pre-conditions checked; nothing installed without explicit consent

Before installing any prerequisite (a language runtime, a CLI tool,
a NuGet/npm/Go package, a binary, anything), the helper or skill
MUST:

1. **Check** whether the prerequisite is already present and at a
   version that satisfies the requirement.
2. **Detect** whether it can be installed automatically on the
   current platform (e.g. via winget, brew, apt, `dotnet tool
   install`, `go install`).
3. **Never install without explicit user permission.** If a
   prerequisite is missing, the helper prints what is missing,
   what would be installed (name, version, scope: user vs system),
   and exits with a non-zero status until the user re-runs with
   an explicit `--install` flag (or equivalent confirmation).

If the prerequisite cannot be installed automatically, the helper
prints the canonical install instructions (link to upstream) and
exits.

**Why:** silent installation is a supply-chain hazard, an
authority escalation, and a UX surprise. Users get to choose what
runs on their machines.

### BR-SECURITY-002 — `disableSkillShellExecution` is honoured

When the managed setting `disableSkillShellExecution: true` is
active, helper scripts MUST NOT run; the skill MUST print a clear
policy message explaining the restriction.

**Why:** enterprises legitimately disable shell-exec; our skills
should degrade gracefully, not silently break.

---

## Adding a new business rule

1. Pick the next free `<NN>` in the relevant area (consecutive,
   no gaps).
2. Write the rule, the consequence (what enforces it, which
   endpoint or component, and what the failure mode is), and the
   `**Why:**` line.
3. In the same PR, add at least one test whose display name starts
   with the new BR ID.
4. CI verifies both files are in lock-step.
