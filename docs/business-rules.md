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
- `DEMO` — `/demo` guided onboarding + integration test.
- `SKILL` — skill-author rules and helper safety.
- `HELPERS` — the .NET deterministic helpers sidecar.
- `SECURITY` — cross-cutting safety constraints.
- `CODE` — code-quality rules that apply to every .NET project in
  the repo (and any future polyglot code).
- `PROCESS` — how changes are made to the project itself
  (planning, gating, git discipline).

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

### BR-OTEL-006 — `/otel up` / `/otel down` own the collector tier's lifecycle

The `/otel` skill MUST own the **full lifecycle of the collector
binary** (the OTEL tenant on top of the deterministic-helpers
platform), routed through the shared `IProcessLifecycle` service
defined in `BR-PROCESS-008`. Concretely:

- `/otel up` — probe collector via `IProcessLifecycle`. If the
  state is:
  - `RunningOurs` — no-op, report PID.
  - `NotRunning` — spawn the configured collector binary
    (`Otel:CollectorExePath` + `Otel:CollectorConfigFile` from
    appsettings, BR-CODE-001), write the PID file, return
    success message.
  - `Zombie` — sweep first, then spawn.
  - `Conflict` — refuse; print the conflict reason and the
    `BR-OTEL-005` recovery options. Never auto-kill an
    unidentified process (BR-SECURITY-003).
- `/otel down` — probe; if `RunningOurs`, kill via
  `IProcessLifecycle.StopAsync` and clean the PID file. If
  `Zombie`, sweep. If `Conflict`, refuse. If `NotRunning`, no-op.

The collector's stdout/stderr are pumped to
`.claude/runtime/collector.log` so the child doesn't deadlock on
full pipes and the user has a single place to read collector logs.

**Why:** before this rule landed, the collector tier had no
lifecycle owner. Bringing the demo up end-to-end required manual
`./dist/.../claude-otel-collector.exe --config=...` and manual
`Stop-Process` cleanup. Now `/otel up` and `/otel down` are the
canonical commands; `/skill-bootstrap` covers the platform tier;
neither skill crosses the platform/tenant boundary
(BR-PROCESS-008 — no project-wide "stop everything" command).

### BR-OTEL-005 — OTLP port-conflict is detected and reported, never silently overrun

Before a skill instructs the user to bring the OTEL collector up
(via `/otel up` once Plan-5 lands; via the docs `HOW TO BRING IT
UP` section in `/demo` until then), the skill MUST probe the
OTLP receiver port (`:4318` by default) for an existing listener
and distinguish two cases:

1. **Port free** — bring-up will succeed.
2. **Port held by the project's own collector** — visible because
   the collector control API on `:13133` is also reachable; this
   is the healthy "already running" case.
3. **Port held by another process** — `:4318` is listening but
   `:13133` is not. Some other OTLP receiver (e.g. another
   collector, an observability tool) owns the port; the project's
   collector cannot bind.

In case 3, the skill MUST:

- Print a clear `PASS|FAIL` row with the conflict named (the row
  ID for `/demo` is `STEP 00.e`).
- Offer the user a structured choice between:
  - stopping the holding process (the user runs the stop command
    themselves — no skill auto-kills another process per
    `BR-SECURITY-003`), or
  - re-porting the project's collector to a different OTLP port
    (config edit, restart).
- Never silently retry, never silently overrun.

Probed by <see cref="IPortProbe"/> (`PortProbe` in production;
mocked at the seam in tests per `BR-PROCESS-007`).

**Why:** before this rule landed, `/demo`'s pre-flight reported
"collector control unreachable on :13133" as the only collector-
related signal. When another OTLP receiver was running on `:4318`,
the user couldn't tell from `/demo`'s output why their collector
wouldn't start — they'd run the start command and get a raw bind
error from the collector binary. This rule catches the case in
the skill layer, where the user's recovery is fastest.

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

### BR-SKILL-009 — `allowed-tools` is the tightest prefix that works

Every `allowed-tools` entry in a SKILL.md MUST name the longest
literal prefix that still lets the skill function. Broad patterns
(`Bash(curl *)`, `Skill`, `Bash(node *)`) are rejected unless a
written justification is added to the SKILL.md frontmatter
explaining why a tighter prefix isn't possible.

Concrete rules:

- HTTP calls: `Bash(curl http://<host>:<port>/<path> *)` — the URL
  must be the first argument after `curl` so the prefix matches.
- Skill chaining: `Skill(<exact-skill-name> *)` — never bare `Skill`.
- Native tool invocations: include enough of the argument prefix
  to scope behaviour (`Bash(git status *)`, not `Bash(git *)`).

The `!` shell-exec preprocessing line is NOT subject to this rule
(it's pre-prompt rendering, outside the permission system); but
any tool calls Claude makes during the skill's active turn ARE
subject. Tight prefixes are defense in depth.

**Why:** broader-than-necessary patterns silently authorise tool
calls the skill never intended. Tight prefixes make the skill's
authority surface visible in one place.

### BR-SKILL-008 — Non-.NET dependencies must be justified

Any tool, language, or runtime in this project that is not .NET
10 MUST come with a written justification in CLAUDE.md naming
what specifically .NET cannot do, or what would be unreasonably
costly to implement in .NET. The justification is reviewed on
every PR that touches the dependency surface.

**Current accepted non-.NET dependencies and their reasons:**

- **Go (OCB / OpenTelemetry Collector framework)** — upstream
  is Go; we can't build an OTel Collector distribution in .NET
  without recreating the entire framework.
- **`curl` (HTTP client for SKILL.md `!` exec lines)** — needed
  in shell to talk to the sidecar from inside a markdown skill;
  ships natively on every supported platform.

Adding any other tool/language/runtime requires updating the
CLAUDE.md list AND adding a passing test that exercises the
dependency. No silent additions.

**Why:** every additional language is a maintenance, security,
and onboarding tax. The bar for adding one must be high and the
reasoning visible.

### BR-SKILL-007 — No per-skill helper code outside the sidecar

Skills MUST be pure markdown + a single shell invocation that
calls the .NET helpers sidecar. Per-skill helper scripts written
in Node, Python, PowerShell, Go, or any third language are
forbidden inside `.claude/skills/<name>/scripts/`. New skill
logic goes in the .NET sidecar behind a new HTTP endpoint, not
in a script.

The single permitted shell tool in `!` exec lines is `curl`
(present on every supported platform). The sidecar's endpoint
takes form-encoded data (`--data-urlencode`) so values pass
through untouched without JSON-escaping in shell.

**Why:** the .NET-sidecar-as-boundary is the project's core
architectural commitment. A third language is exactly the
duplication / drift / test-surface bloat that boundary exists
to prevent.

### BR-SKILL-006 — Deterministic work uses the .NET sidecar

Skill helpers must NOT ask the LLM to perform deterministic work.
Deterministic operations (parsing, validation, scanning, slug
normalisation, config probing, git-status parsing) MUST call the
.NET helpers sidecar.

**Why:** reproducibility, cost, speed, audit, and security — see
README's "Single sidecar for deterministic work — pros and cons".

### BR-DEMO-002 — `/demo` is a pure skill-chain orchestrator

`/demo` MUST invoke every action through another skill's
dispatch endpoint via `ISkillDispatchClient`. It MUST NOT call:

- the collector control client (`ICollectorControlClient`) for
  any action — only `IsHealthyAsync` (a status probe) is
  permitted, and only inside the pre-flight section;
- vendor HTTP APIs (e.g. `wttr.in`) directly;
- the OTLP receiver (`:4318`) directly.

Read-only observation steps that summarise the *result* of
upstream skill calls (e.g. counting JSONL records by ticket ID)
are permitted as direct file reads — they verify, they don't act.

This makes `/demo` simultaneously:

- a **demonstration** of skill chaining (every action step is a
  loopback call to another skill's dispatch endpoint),
- the project's **full-stack integration test surface** —
  exercising the entire skill stack including parsing,
  validation, and underlying contracts, not just the collector.

**Why:** if `/demo` bypassed skills and called the collector
directly, it would only test the collector contract; a parsing
bug in `/otel` or a validation bug in `/enrich` could ship
undetected. By going through skills, every `/demo` run exercises
the same code path the user would. Captured against the
`DemoDispatchEndpoint` source via tests `BR-DEMO-002 — ...` in
`DemoDispatchEndpointTests`.

### BR-EXTEND-010 — Domains expose their guided demo via `IDomainDemo`

A domain SHOULD register an `IDomainDemo` companion contract
alongside its `IDomain` implementation. The companion is
**opt-in** (a domain may register `IDomain` without `IDomainDemo`
if it has nothing to demo) but recommended — a demo is the
canonical first-run user experience for a domain.

`IDomainDemo` exposes the **live skill-chain section** of `/demo`
only. The platform-level pre-flight (sidecar reachable, collector
control, output dir, persistent file, OTLP port — `STEP 00.x`
rows) and the teardown section live in `DemoDispatchEndpoint`
because they are platform concerns, not domain ones.

Contract:

```csharp
public interface IDomainDemo
{
    string DomainName { get; }
    Task<IReadOnlyList<DemoStepResult>> RunAsync(DemoContext ctx, CancellationToken ct = default);
}

public sealed record DemoContext(string SessionId, ISkillDispatchClient Skills);
public sealed record DemoStepResult(int Number, string Label, bool Pass, string Detail);
```

Discovery is via DI: the dispatch endpoint takes
`IEnumerable<IDomainDemo>` and selects the first whose
`DomainName` matches the requested domain. Absence renders a
"no demo for domain X" notice and falls through to the teardown
section.

The `OtelDomainDemo` implementation walks 14 live steps
(BR-DEMO-001): `/otel up` → 3× `/otel set` → `/otel get` round-
trip → 2× `/enrich` → 4× `/weather` → 2× JSONL observation →
`/otel down`. Every action step chains via `ISkillDispatchClient`
(BR-DEMO-002 — pure orchestrator).

**Why opt-in:** not every domain has a demonstrable workflow.
Forcing every `IDomain` to ship demo steps would couple the
contract to a use-case that may not apply (e.g. a future
information-only domain). Splitting `IDomainDemo` from `IDomain`
keeps each concern independent.

### BR-DEMO-003 — `/demo` dispatch never propagates exceptions

The `/demo` dispatch handler MUST always return HTTP 200 with a
structured PASS|FAIL response, even when an underlying observation
step encounters an IO error (e.g. the collector holds
`output/telemetry.jsonl` open for append and a naive read with
default share mode would throw). Catching and converting these
errors to a FAIL row with the reason inline is part of the
handler contract.

Concretely:

- All file reads inside `/demo` use `FileShare.ReadWrite |
  FileShare.Delete` to coexist with concurrent writers.
- All IO is wrapped in a try-catch that produces a FAIL row with
  the exception message in the detail field.
- The handler returns 200 with the rendered text whether every
  step PASSed or some FAILed; FAIL is data, not failure.

**Why:** the handler throwing a 500 violates BR-DEMO-001 (every
step must emit a marker; final summary must be parseable) and
breaks the BR-PROCESS-007 promise that `/demo` doubles as the
project's integration test surface — a 500 has no structured
content for a CI check to assert against. Capturing every
failure as a FAIL row inside the response keeps the contract
honest under all real-world conditions.

### BR-DEMO-001 — `/demo` is a guided onboarding tour and integration test

`/demo` MUST emit:

1. A **pre-flight section** with `STEP 00.x: PASS|FAIL — <detail>`
   rows (currently `00.a` sidecar, `00.b` collector control,
   `00.c` output dir, `00.d` persistent-enrichments file).
2. A `PRE-FLIGHT RESULT: x/y PASS` summary line.
3. **When the collector is down**, a `HOW TO BRING IT UP` section
   with the exact commands to start the missing components, and
   `DEMO RESULT: 0/12 PASS` (the 12 live steps are skipped).
4. **When the collector is up**, a `LIVE DEMO STEPS` section with
   12 rows in the format `STEP NN: PASS|FAIL — <detail>`, where
   `NN` is `01..12`, plus a `DEMO RESULT: x/12 PASS` summary.
5. A `TEARDOWN` section explaining how to reverse the demo.

The marker format is the contract: every line that begins
`STEP <id>: PASS` or `STEP <id>: FAIL` is machine-parseable so the
same dispatch endpoint doubles as the project's end-to-end
integration test surface.

**Why:** `/demo` serves two audiences. A new user runs it on a
clean machine; the pre-flight FAIL rows + install instructions
are how they learn what to install and how to start it. A
contributor runs it as the project's smoke-test; the stable
markers and final summary line make the result diff-able and
CI-checkable. One skill, two audiences, one output format.

### BR-SKILL-010 — Every dispatching skill has a precondition fallback

Every skill whose `!` preprocessing line dispatches via the
deterministic-helpers sidecar (`curl http://127.0.0.1:5050/skills/<name>/dispatch`)
MUST end the line with `|| printf 'PRECONDITION_FAIL: ...'`,
where the message refers the user to `/skill-bootstrap`. Concrete
canonical form:

```
|| printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'
```

The skill body MUST also include an instruction to render the
`PRECONDITION_FAIL` line and stop, so Claude does not attempt
the skill's actual work when the sidecar is down.

The `--max-time 5` flag SHOULD be present so that a hung socket
fails fast instead of stalling the user.

**Single named exemption:** `/skill-bootstrap`. Its `!` line
probes `:5050/healthz` directly with its own `||` fallback,
because its job is to bring the sidecar up; it cannot route
through the very thing it is trying to start.

**Why:** every dispatching skill failed identically before this
rule landed — `curl: (7)` to stderr, `!` exec aborts, skill body
never reaches Claude, user sees no actionable next step. The
fallback guarantees the `!` exec exits 0, the body always
reaches Claude, and the user always sees a one-line instruction
to fix the precondition. Enforced by lint test
`SkillPreconditionLintTests`.

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

## CODE

### BR-CODE-001 — No magic strings/numbers in code; settings live in config

Anything that varies by environment, deployment, or future tuning
MUST be a setting bound from `appsettings.json` (or environment /
secret store) onto a typed options class. Code references the
typed options, never the literal value.

What MUST be a setting (non-exhaustive):

- URLs, hostnames, port numbers
- Timeouts, retry counts, polling intervals
- File paths and directory roots
- Cache TTLs
- Feature flags
- Resource limits (max value length, max upload size, etc.)

What is NOT a "magic string/number" and is allowed inline:

- HTTP method names (`"GET"`, `"POST"`) — invariant by HTTP spec.
- JSON property names parsed from a fixed external schema (OTLP,
  OpenAPI, vendor APIs).
- Regex patterns that ARE the rule being enforced (e.g.
  BR-ENRICH-001's key regex — the regex *is* the BR).
- Test fixture data scoped to one test class.
- User-facing message text (a v2 candidate for i18n; tracked
  but not blocking now — the message MUST be a `const` string
  in one place per consumer, never duplicated).

Each typed options class lives next to the consumer it configures
(`Infrastructure/CollectorOptions.cs`, etc.) and is wired via
`builder.Services.Configure<T>(builder.Configuration.GetSection("X"))`
+ `IOptions<T>` injection.

**Why:** strings inline in code are invisible config — they survive
deployment as if they were source. A reviewer reading the source
can't tell what's tuneable, an operator can't change behaviour
without a rebuild, and tests can't isolate the system from real
external services without monkey-patching. Typed options surface
the configurable surface.

### BR-CODE-002 — Configuration files load from the binary's directory

When the sidecar is invoked as `dotnet HelpersSidecar.dll` from
any working directory, `appsettings.json` and the
`appsettings.{Environment}.json` overlay MUST load from
`AppContext.BaseDirectory` (where the DLL lives), in addition
to the content root.

**Why:** without this rule, a developer override in
`appsettings.Development.json` is silently ignored when the
sidecar is started from a non-default working directory. The
sidecar starts cleanly; configured values default; the failure
is invisible until someone notices configured behaviour has
not taken effect. The two-source loading pattern (binary-dir
for *configuration*, content-root for *working files* like
`output/` and `persistent-enrichments.json`) keeps deployment
flexible without losing dev overrides.

The wire-up lives in `Program.cs`:

```csharp
var binDir = AppContext.BaseDirectory;
builder.Configuration
    .AddJsonFile(Path.Combine(binDir, "appsettings.json"),
                 optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(binDir, $"appsettings.{builder.Environment.EnvironmentName}.json"),
                 optional: true, reloadOnChange: false);
```

**Defect of origin:** `c4fccf4` (2026-05-03). The dev override
of `Otel:CollectorOtlpPort=14318` in `appsettings.Development.json`
was being dropped when the sidecar was started from the project
root — the `/demo` pre-flight 00.e probed the wrong port and
reported a conflict the dev environment had already worked
around.

### BR-CODE-003 — Process spawning resolves paths to absolute before Process.Start

When `ProcessLifecycle.SpawnAsync` (or any future spawn site)
launches a child process, the executable path MUST be resolved
to an absolute path via `Path.GetFullPath` before constructing
the `ProcessStartInfo`. The spawn site MUST also probe
`File.Exists` against the resolved path and return a
`SpawnResult(Spawned: false, ..., Reason: "exe not found: …")`
instead of allowing `Process.Start` to throw a generic
"system cannot find the file specified".

**Why:** `Process.Start` on Windows does not reliably resolve
forward-slash relative paths against the configured
`WorkingDirectory`. The failure mode is silent and
platform-dependent: the same `ComponentSpec.ExePath` works on
one OS and fails on another, producing an opaque Win32 error
that names the symptom (file not found) without naming the
cause (path resolution).

Resolving to absolute at the spawn site eliminates the
platform inconsistency. The `File.Exists` pre-check converts
the failure into a typed result a caller can render to the
user without wrapping a try-catch around every spawn.

**Defect of origin:** `c4fccf4` (2026-05-03). `/otel up`
failed with "An error occurred trying to start process
'dist/windows-amd64/claude-otel-collector.exe' … The system
cannot find the file specified" even though the exe was
present and the working directory was correct — the slash-
direction in the relative path was the cause.

## PROCESS

### BR-PROCESS-001 — Skill changes go through `/otel-extend`

Any change touching `.claude/skills/**`,
`src/HelpersSidecar/Endpoints/*DispatchEndpoint.cs`, or
`src/HelpersSidecar/Application/*Verb.cs` MUST be made via the
`/otel-extend` flow (plan → implement → build → test, each phase
gated by explicit user confirmation and committed separately).

Hand-rolled commits to those paths are forbidden, with **named
bootstrap-class exceptions** — skills that must exist before the
flow that would govern their creation can run. Each exception
must be listed here, justified in `docs/process-incidents.md`,
and produced as exactly one named commit.

**Currently named exceptions:**

1. The commit that *builds* `/otel-extend` itself. The flow can't
   govern its own creation.
2. The commit that *builds* `/skill-bootstrap`. The
   deterministic-helpers sidecar at `:5050` hosts every dispatch
   endpoint that `/otel-extend` routes through. If the sidecar is
   not running, `/otel-extend` cannot dispatch, so the skill that
   exists to bring up the sidecar (`/skill-bootstrap`) cannot be
   built through `/otel-extend`. One explicit hand-rolled commit
   resolves the chicken-and-egg.

Both exceptions share the same shape: the committed skill is
itself the bootstrap mechanism for some downstream rule. Future
bootstrap-class skills follow the same shape — one explicit
named exception per skill, each justified in
`docs/process-incidents.md`. The bar for a third exception is
high: the skill must demonstrably be a prerequisite for an
existing rule's enforcement, not merely "convenient".

The complete procedure (decision tree, phase-by-phase) lives in
`CLAUDE.md` under "Skill changes go through `/otel-extend`". The
incidents that motivated this rule are documented in
`docs/process-incidents.md`.

**Why:** plan-document-per-change, per-phase gates, and
per-phase commits are not paperwork — they are the only way a
self-modifying project keeps a clean revert story and a visible
authority trail. Without this rule, "small" changes accumulate
silently and a future operator can't tell why the system looks
the way it does. Bootstrap exceptions are the smallest possible
deviation from the rule: each one named, each one justified.

### BR-PROCESS-003 — Evidence-driven promotion and demotion

Strategies proposed in retros graduate to business rules by
accumulating evidence; rules violated under the same machinery
get a forced review. Both directions use one settable schema.

`evidence.stages` is an ordered array of `{ gate, min }` pairs.
A strategy progresses through stages in order; each stage
requires `min` independent occurrences passing its `gate` before
the strategy moves on. When the final stage's `min` is met, the
strategy is promotable in the next retro.

**Default:**

```yaml
evidence:
  stages:
    - gate: "concrete-and-testable"; min: 1
    - gate: "applied-in-real-change"; min: 3
    - gate: "no-rework-no-violation"; min: 3
```

**Settable** at two levels (most-specific wins):

- Per skill — `evidence:` block in SKILL.md frontmatter, applies
  to strategies scoped to that skill.
- Per strategy — `evidence:` block inline in `docs/retros.md`,
  overrides the skill-level and the default.

Counts live next to the strategy in `retros.md`
(`stage[applied-in-real-change] 2/3 in commit <sha>`); no hidden
state. The full procedure for promotion (write the BR + its
test, archive the strategy entry) is in CLAUDE.md under
"Evidence-driven rule promotion".

Demotion mirrors promotion: a BR violated against the final
stage's `min` count prompts a **forced review** — not
auto-demotion — at which point the reviewer keeps, fixes, or
demotes.

**Why:** this turns the rule register into a downstream artefact
of empirical observation, not a speculative pile. Strategies
that don't survive contact with reality get culled rather than
ossifying. Rules that are violated under their own threshold
prove themselves wrong by the same machine.

### BR-PROCESS-004 — Evidence sources are configurable per gate

Each gate in an `evidence.stages` array declares its `source` —
the mechanism that produces the count for that gate. Sources can
be deterministic (no human required) or human-in-the-loop.

Supported sources (default if omitted: `hitl-retro`):

- `hitl-retro` — a human writes a retro entry attesting the
  gate passed.
- `otel-query` — a query against `output/telemetry.jsonl`
  returns the count. The project's own telemetry is its own
  evidence; this is the canonical source for skill-related
  gates because skill invocations already emit
  `claude_code.skill_activated` events that carry session,
  prompt, and tool-input attributes.
- `ci-signal` — a named test passes. The test must exist before
  the gate can use it.
- `command-probe` — a command exits 0.

For OTEL-query gates the schema includes `query` (event name +
where clause) and optionally `select` (which attributes to pull
back for the retro to display). Selection lets a retro author
read the actual input/output shape of a skill run while
counting it as evidence.

The full schema definition with worked examples lives in
CLAUDE.md under "Evidence sources can be deterministic or HITL".

**Why:** judgement gates and machine gates serve different
needs and shouldn't be modelled the same way. A retro author
should never have to hand-count event records the system
already has structured. A retro author should also never be
forced to encode an inherently subjective judgement as a
deterministic check. Mixing the two cleanly makes the rule
register honest about what each piece of evidence actually
means.

### BR-PROCESS-006 — Evaluate changes from ≥ 3 orthogonal perspectives

When recommending or evaluating any architectural change (a
language pivot, a dependency add, a deployment-shape change, a
process change of meaningful scope), the analysis MUST surface
pros and cons from **at least three orthogonal perspectives**.
"Orthogonal" means genuinely different lenses, not three
sub-views of the same one.

**Standard orthogonal lens set** (pick at least three; add more
if relevant):

- **Engineering** — code we author/maintain, test coverage,
  language/toolchain burden, refactor cost.
- **Operations** — how it runs in production, deployment shape,
  failure modes, debugging, operator familiarity, vendor docs
  alignment.
- **Strategy** — alignment with project goals, future-proofing,
  ecosystem coupling, lock-in risk, optionality.
- **User-facing** — capabilities exposed, ergonomic friction,
  edge-case behaviour.
- **Security** — threat model surface, attack vectors,
  trust-boundary implications.
- **Cost** — engineering time, runtime resources, third-party
  fees, learning curve for new contributors.

A recommendation that surfaces pros only — or surfaces three
"perspectives" that are all sub-views of engineering — does not
satisfy this rule.

**Why:** single-perspective analysis hides whole categories of
loss until challenged. The bias is asymmetric: gains tend to be
visible from the recommender's frame; losses live in adjacent
frames the recommender hasn't taken. Forcing at least three
orthogonal lenses turns "what could go wrong?" into a checklist
instead of an exercise in foresight.

### BR-PROCESS-005 — Flag architectural decisions; document why we deviated

Any decision that introduces a new language, runtime, framework,
load-bearing library, deployment model, or storage shape MUST
be flagged for explicit user confirmation **before** it lands in
the plan or in code. "Significant" = expensive or disruptive to
reverse.

The flag must include:

1. The decision in one sentence.
2. **Alternatives enumerated with research, not from memory.**
   If you haven't checked whether a viable alternative exists,
   say so before deciding.
3. Trade-offs of each alternative.
4. A recommendation with explicit reasoning.
5. An "OK to proceed?" gate.

When a path is chosen, **document the deviation**: if the
selected option departs from a documented standard, a prior
convention, or "what the project's primary stack would
otherwise look like", record:

- The departure in **the commit message** that introduces the
  decision (one paragraph: what we deviated from, why, what we
  accept as the cost).
- A pointer in **`docs/business-rules.md`** or **CLAUDE.md** if
  the decision is load-bearing enough that future contributors
  need to find it without reading commit history.

For the language-choice case specifically, this rule sits on
top of `BR-SKILL-008` (which already requires non-.NET
dependencies be justified). `BR-PROCESS-005` generalises the
requirement to every architectural choice, not just language.

**Why:** silent architectural choices accumulate into a stack
of "well, that's how we ended up doing it" without ever having
been weighed. The cost of a two-line flag and a one-paragraph
deviation note is negligible; the cost of pivoting after the
choice has propagated is not.

### BR-PROCESS-008 — Each tier-managing skill owns its tier's process lifecycle

Every skill that spawns a long-running process MUST own that
process's full lifecycle: **probe → spawn → stop → zombie sweep**.
The skill owns the *named tier* (sidecar, collector, etc.); the
shared deterministic pattern lives in the helpers sidecar's
`IProcessLifecycle` service consumed by every lifecycle-managing
skill.

Concrete tier ownership in v1:

- **`/skill-bootstrap`** — owns the sidecar tier (`:5050`). On
  `start` it probes the lifecycle CLI (`--lifecycle probe sidecar`),
  sweeps zombies if the state is `Zombie`, then spawns. The
  sidecar binary writes its own PID file at
  `.claude/runtime/sidecar.pid` on startup and clears it on
  graceful shutdown.
- **`/otel up` / `/otel down`** — will own the collector tier
  once Plan-5 (the .NET-only collector pivot) lands. Until then,
  the collector tier is unowned and the user starts/stops it
  manually.

**No project-wide "clean everything" command.** Cleaning across
tiers crosses the platform/tenant boundary; that crossing should
be explicit (`/otel down` then `/skill-bootstrap stop`), not
hidden behind one magic verb. Adding a new tier always means
adding the lifecycle ownership to the right tier-skill, not
expanding a single shared command.

**The lifecycle state machine** (`LifecycleState` enum):

| State        | PID file | PID alive | Port held | Meaning                                  |
|--------------|----------|-----------|-----------|------------------------------------------|
| `NotRunning` | absent   | n/a       | no        | clean slate; spawn will succeed          |
| `RunningOurs`| present  | yes       | yes       | already up; do nothing                   |
| `Zombie`     | present  | yes       | no        | process exists but isn't bound — sweep   |
| `Zombie`     | present  | no        | no        | stale PID file; sweep deletes it          |
| `Conflict`   | absent   | n/a       | yes       | someone not ours owns the port           |
| `Conflict`   | present  | no        | yes       | PID file stale, port held by another     |

Sweep kills only zombies the file identifies as ours; never the
process listening on the port if it isn't already in our PID file
(`BR-SECURITY-003` — no auto-kill of unidentified processes).

**Why:** before this rule landed, every `/skill-bootstrap start`
required the user to manually `Stop-Process` any sidecar from a
prior session, and the file `:5050` was bound to was indistinguish-
able from a zombie at the skill layer. Each tier needing its own
ownership of process state — with one shared deterministic pattern
in the sidecar — keeps the state machine in one place while
respecting the platform/tenant boundary the project commits to.

### BR-PROCESS-007 — Tests scope to one domain change

Every test in this project MUST scope to a single domain
change. Cross-domain dependencies (e.g. an orchestrator skill
that chains into other skills, an endpoint that calls a vendor
API, a processor that reads upstream state) MUST be **mocked
or stubbed at the seam**, not exercised end-to-end inside the
default test loop.

Concretely:

- `/demo`'s tests mock `ISkillDispatchClient` so a change to
  `/otel`'s parser, `/enrich`'s validator, or `/weather`'s
  vendor call does NOT re-run `/demo`'s tests. Each downstream
  skill has its own domain test.
- An endpoint that calls the collector control client mocks
  `ICollectorControlClient` to scope the test to the endpoint's
  own logic.
- A processor that reads from disk mocks the file IO layer.

**The "one domain change → minimum test surface" loop** is the
goal. If a change to a single domain triggers re-runs of
unrelated domains' tests, the test boundaries are drawn wrong.

End-to-end tests that genuinely span domains (full integration
loops) are permitted, but they MUST be tagged
`[Trait("Scope", "cross-domain")]` and excluded from the
default `dotnet test` filter. They run on demand, not every
loop. The runtime invocation of `/demo` against a live sidecar
+ collector serves the same purpose without a slow test class.

**Why:** a fast integration loop is a hard requirement for the
TDD/DDD discipline the project commits to. The longer a loop
takes, the less it gets run; the less it gets run, the less it
catches. Domain-scoped tests stay milliseconds-fast. Cross-
domain tests stay opt-in. The two-tier structure is the only
way the project keeps a sub-10-second `dotnet test` round-trip
as the codebase grows.

### BR-PROCESS-002 — Retro after every requested change

After every user-requested change of meaningful scope, the
response MUST end with a brief retrospective covering:

- **What happened** — the change and any friction encountered.
- **What could be improved** — process gaps, missed
  opportunities.
- **Strategies for next time** — concrete actions or rules that
  would prevent the same friction.

Three sections, bullet points, ~200 words total. No platitudes.
A strategy that's generic ("communicate better") either gets
made concrete or is dropped.

Substantial retros also append to `docs/retros.md` (newest first)
so future contributors can read the operating history of the
project rather than relying on the current author's memory.

**Why:** retros are how a project notices its own friction.
Without one per change, lessons evaporate the moment the diff is
merged. With one per change, the same surprise rarely happens
twice.

### BR-PROCESS-010 — Defects that encode generalisable invariants must be captured as BRs

A bug fix is *also* a rule discovery whenever the fix encodes a
constraint future code must follow. The author MUST capture the
constraint as a `BR-*` (with at least one test proving it) when
either:

- **(a)** the fix encodes a generalisable invariant — i.e. the
  same defect could recur in any new code path that follows the
  same pattern; OR
- **(b)** the failure mode is silent / platform-dependent — i.e.
  the next person to hit it would have no obvious diagnostic.

The BR lands in **the same commit** as the fix when practical, OR
in a follow-up `docs(br):` commit that cites the originating fix
commit SHA as the "defect of origin".

**Exempt** — pure value-corrections that don't encode a rule:
typos, off-by-one mistakes scoped to one call site, a wrong
literal that was wrong-once, a one-time data migration. These
are bug fixes only.

**Why:** without this rule, a defect's *cause* (the
unspoken-rule-the-code-violated) walks out of the project as
soon as the fix lands, leaving only a `fix:` commit message that
describes the symptom. The next contributor reinvents the same
mistake because the rule was never written down. A BR makes the
cause auditable; a test makes the rule self-defending.

The rule itself was discovered by the project's own friction:
two defects in commit `c4fccf4` (appsettings cwd vs binary-dir;
Process.Start path resolution on Windows) landed without their
BRs. The BRs were added in `d10cb3d` as `docs(br):` follow-ups
citing `c4fccf4`. That experience is what `BR-PROCESS-010`
encodes: the next time, the slip is named *as* a slip.

**Enforcement:** reviewer discipline today; a future commit-
message lint test could enforce that any `fix:` commit either
adds a `BR-*` reference in its body OR is followed within N
commits by a `docs(br):` commit naming it as defect-of-origin.
Out of scope for v1 of this rule.

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
