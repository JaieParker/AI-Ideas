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

### BR-OTEL-007 — Collector ports have a single source of truth

**Every** port the Go OTel collector binds (or that downstream
exporters target) has exactly one source of truth: the .NET
sidecar's typed `CollectorOptions` class, bound from the `Otel`
section of `appsettings.json`. The four ports under this rule:

| Option key                            | Default | Role                                  | Env var (sidecar → collector)         |
|---------------------------------------|---------|---------------------------------------|---------------------------------------|
| `Otel:CollectorOtlpPort`              | 4318    | OTLP HTTP receiver bind               | `CLAUDE_OTEL_OTLP_HTTP_PORT`          |
| `Otel:CollectorControlPort`           | 13133   | enrichmentctl control extension bind  | `CLAUDE_OTEL_CONTROL_PORT`            |
| `Otel:CollectorHealthzPort`           | 13134   | health_check extension bind           | `CLAUDE_OTEL_HEALTHZ_PORT`            |
| `Otel:DownstreamOtlpPort`             | 4319    | otlphttp exporter target              | `CLAUDE_OTEL_DOWNSTREAM_OTLP_PORT`    |

When `ProcessLifecycle.SpawnAsync("collector")` launches the
collector, it MUST set every env var above on the child
`ProcessStartInfo` from the resolved option. `config.yaml` MUST
consume each via OTel-collector-native substitution
(`${env:NAME:-default}`). The sidecar's own consumers
(`CollectorControlClient`, dispatch endpoints' error messages,
`/demo`'s pre-flight) MUST read the value from
`IOptions<CollectorOptions>`, never an inline literal.

Local users who need a different port edit exactly one place:
a gitignored `appsettings.Local.json` next to the sidecar's
`appsettings.json`. The sidecar's config builder loads it via
`AppContext.BaseDirectory` per `BR-CODE-002`. No edit to
`config.yaml`, no environment-variable toggle, no duplicate
config file.

**Forbidden:**

- Hardcoded `4318`, `13133`, `13134`, `4319` (or any other
  collector-port literal) outside `appsettings.json`'s default
  values and `CollectorOptions`'s `= <default>` initialisers.
- Hardcoded port literals in any description string, log
  message, error message, or comment that the user might read
  and trust.
- Probing a fixed port in any pre-flight check or HTTP-client
  base URL; every reference reads the resolved typed option.

**The canonical env-var name CONSTANTS** live as `public const
string` fields on `ComponentRegistry` (`CollectorOtlpPortEnvVar`,
`CollectorControlPortEnvVar`, `CollectorHealthzPortEnvVar`,
`DownstreamOtlpPortEnvVar`). Any code that reads or writes the
env var MUST reference the constant — keeping the .NET spawn
site and a test that asserts the env-var-name agreement against
`config.yaml` in lock-step.

**Why:** before this rule landed, the OTLP port lived in three
places (`config.yaml`, `appsettings.json`, an environment-gated
`appsettings.Development.json` + `config.acceptance.yaml`
duplicate) that drifted silently when a user re-ported locally
because `:4318` was held. The other three collector ports
(`:13133`, `:13134`, `:4319`) lived in even more places —
hardcoded in `CollectorControlClient`, in dispatch error
messages, in `IPortProbe` comments. The single-source mechanism
eliminates the drift class for every collector port at once.

**Test target:** `DemoPortProbeFollowsConfigTests`,
`CollectorSpawnPropagatesPortEnvTests`,
`CollectorControlClientReadsConfigTests` (all under
`tests/HelpersSidecar.IntegrationTests/`). Manual:
`grep -rn '"4318"\|"13133"\|"13134"\|"4319"' src/ config.yaml`
should return zero matches outside `appsettings.json`'s
defaults and `CollectorOptions`'s initialisers.

**Defect of origin:** Plan-13 (2026-05-04). `/demo otel`
pre-flight reported a `:4318` conflict held by
`ClaudeObserver.Api`; the user re-ported `config.yaml` to
`:14318`, but the sidecar (running in Production env, no
`appsettings.Development.json` loaded) still probed `:4318`
because the typed option's value hadn't moved with the YAML
edit. Mid-Phase-2 of Plan-13, the user surfaced that the same
defect class applied to the other three collector ports; this
rule was widened from "OTLP port" to "every collector port".

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

### BR-EXTEND-004 — Plan numbering is consecutive within each domain

Plan files are named per the resolved domain's
`PlanFileConventions` (e.g. OTEL: `The-OTEL-Plan(-<N>(-<slug>))?.md`,
kai-platform: `The-KaiPlatform-Plan(-<N>(-<slug>))?.md`). The next
available number is the maximum of existing N values **within the
domain's `PlanFileConventions.Directory`** plus 1, or `NumberFloor + 1`
if only the base file exists. Gaps (e.g. 1, 3, 5) are not skipped
— the next is still max+1.

Numbering is **per-domain**. OTEL's count and kai-platform's count
are independent — each domain's `PlanFiles.Directory` is the unit
of "consecutive". Cross-domain plans (under
`docs/cross-domain/plans/`) follow their own counter.

**Why:** predictable numbering within a bounded context. Domains
that don't yet exist or that haven't started planning don't dictate
counters for each other; each context's history is self-coherent.

**Plan-9 amendment:** the original rule read "consecutive across
the project". Plan-9 amended to "consecutive within each domain"
to match the per-domain plan directory layout
(`docs/<domain>/plans/`). Resolution recorded in
`docs/otel/plans/The-OTEL-Plan-9-domain-localised-plan-files.md`'s
"Architecture review decisions" section as **Evolve**, justified
by the bounded-context principle (Fowler's `BoundedContext.html`
in OtelDomain.TrustedReferences).

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

### BR-EXTEND-006 — Domains expose flow configuration via `IDomain`

Each project domain MUST register a singleton implementation of
`IDomain` in DI. Consumers (`/extend-skills`, `/demo`, future
`/architecture-review`, `/domain-info`) resolve domains by name
through `IDomainResolver`. Adding a new domain is one new class
plus one DI registration; no consumer changes.

`IDomain` is a **knowledge facade**, not a registry. Each slice
is a typed contract:

| Slice                  | Type                                       | Why |
|------------------------|--------------------------------------------|-----|
| `Name`                 | `string`                                   | Stable identifier — first arg to `/extend-skills`, `/demo`, `/domain-info`. |
| `PlanFiles`            | `PlanFileConventions`                      | Plan-file naming (Prefix, NumberFloor, **Directory**) — Plan-9 added Directory for per-domain plan-file location. |
| `Commits`              | `CommitConventions`                        | Per-phase commit prefixes (BR-EXTEND-002). |
| `GovernedGlobs`        | `IReadOnlyList<string>`                    | Path globs the extend flow governs (BR-PROCESS-001 scope). |
| `PlaybookPath`         | `string`                                   | Domain's flow playbook (Plan-9: per-domain location at `docs/<domain>/playbook.md`). |
| `Glossary`             | `IReadOnlyDictionary<string, string>`      | Domain's ubiquitous-language terms. |
| `BusinessRulesPath`    | `string`                                   | BR document for the domain. |
| `TrustedReferences`    | `IReadOnlyList<TrustedReference>`          | Curated authoritative external sources (BR-EXTEND-008). |
| `Probe()` *(opt)*      | `DomainHealth`                             | Self-diagnostic. Default returns Unknown. |
| `PorousBoundaries` *(opt)* | `IReadOnlyList<string>`                | Other domain names this domain is legitimately porous with. Default empty. |

The interface is the **contract**; each domain owns its
**content**. No central authority/registry stores domain knowledge —
the domain implementation IS the source.

**Why decentralised interface (rather than centralised registry):**
matches the project's tier philosophy (`/skill-bootstrap` owns
sidecar; `/otel` owns OTEL tenant; each `IDomain` owns its
knowledge). Bounded by what consumers actually need. Compile-time
enforcement of new-domain contract. Default-implementable
optional members.

### BR-EXTEND-007 — Domain-neutral skill names when generic

A skill that operates uniformly across every registered
`IDomain` MUST take a domain-neutral name (e.g. `/extend-skills`,
not `/otel-extend`); the first user-facing argument names the
domain. A skill that is genuinely domain-specific (operates only
on one domain's invariants — e.g. `/otel set` which mutates the
OTEL collector's enrichment state) MAY keep the domain in its
name.

Renamed in Plan-5 Phase 2c: `/otel-extend` → `/extend-skills`;
the bootstrap-exception-class skill `/otel-extend` (named for
its time) keeps its historical name in `BR-PROCESS-001`'s
exception list. Going forward, the rule applies on every new
skill: if the skill is generic, the name is generic.

**Why:** name reflects scope. A skill named `/otel-extend` reads
as OTEL-specific even when its behaviour is generic. Misleading
names ossify into misleading behaviour as contributors add
OTEL-shaped quirks "because the name says it's OTEL".

### BR-EXTEND-009 — Plan-implementation sessions are tagged with the plan filename

When `/extend-skills <domain> <topic>` is invoked, the flow's
Phase 0 / pre-flight section emits a structured
`PLAN_TAG_ENRICHMENT` directive — the user runs `/enrich plan
<full-plan-filename>` verbatim before Phase 1 begins. Every OTEL
record emitted during the plan's life — drafting, implementing,
building, testing, retro — carries this attribute.

Per-plan filtering becomes one grep:

```bash
grep '"plan":"The-OTEL-Plan-N-...md"' output/telemetry.jsonl
```

When work happens **outside** `/extend-skills` (a manual hot-fix,
a plan-less commit), the user runs `/enrich plan <filename>`
themselves before the work. The architecture-review agent
(`BR-PROCESS-009`) cites the value of `plan` from the current
session's enrichment so its findings tie back to the exact plan
that triggered them.

**Why:** per `BR-PROCESS-004`, the project's own telemetry is
its evidence. Per-plan filtering lets retros, audits, the
architecture-review agent, and Plan-8's per-step demo reports
query exactly the activity for one plan without session-id
archeology. The cost is one extra POST per session start; the
benefit compounds over the project lifetime.

---

## SKILL

### BR-SKILL-001 — `$ARGUMENTS` single-quoted in `!` shell exec

Every `!`-prefixed shell-exec line in any SKILL.md in this repo
MUST single-quote `$ARGUMENTS`. Double quotes (or no quotes) are
forbidden in this position.

**Why:** Claude Code substitutes `$ARGUMENTS` verbatim before bash
sees the command. With double quotes, bash will evaluate `$(…)` and
backticks inside the substituted string — an RCE primitive.

### BR-SKILL-002 — Side-effecting skills set `disable-model-invocation: true`

Any skill with **state-changing side effects** MUST set
`disable-model-invocation: true` so Claude cannot invoke it
without explicit user action. State-changing side effects are:

- File writes outside `output/<owner>/` (the project's
  conventional report directory).
- Network calls beyond local sidecars on `127.0.0.1`.
- Mutations to persistent enrichments
  (`persistent-enrichments.json`).
- Process spawn or kill (lifecycle verbs).
- Any external-system mutation (git operations, vendor APIs,
  message sends).

**Read-only review/report skills are exempt.** A skill whose
only output is a *report* — whether the report lands in
`output/<owner>/` or comes back inline as the dispatch response —
MAY set `disable-model-invocation: false` so chained flows
(e.g. `/extend-skills` Phase 1.5 invoking `/architecture-review`)
can call it. The report contents *are* the output; whether the
file is written or only returned in memory is a deployment
detail, not a category change. The canonical exempt skill is
`/architecture-review`: it loads context, prompts Claude, and
emits a structured response — no state change anywhere.

**Why:** the spirit of the rule is "Claude cannot trigger
destructive flows on its own initiative". A read-only judgement
skill cannot be destructive by construction. Forcing every
read-only skill to require manual user typing breaks legitimate
chained workflows that the project's playbook already expects
(e.g. `BR-PROCESS-009`'s Phase 1.5 was always meant to be
chained from `/extend-skills`).

**Defect of origin:** Plan-13's Phase 1.5 (2026-05-04) attempted
to chain `/architecture-review` from `/extend-skills` via the
Skill tool and was blocked by the original BR-SKILL-002 wording.
The amendment carves the read-only case out of the rule's
literal text rather than relying on case-by-case overrides.

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

### BR-SKILL-007 — No per-skill helper code outside the sidecar (Plan-23 amended)

Skills MUST be pure markdown. Per-skill helper scripts written
in Node, Python, PowerShell, Go, or any third language are
forbidden inside `.claude/skills/<name>/scripts/`. New skill
logic goes in the .NET sidecar behind a new HTTP endpoint, not
in a script.

**Two skill shapes** are permitted:

1. **Dispatching skills (default).** A SKILL.md `!` exec line
   curls the sidecar at render time, baking the dispatch
   response into the prompt. The single permitted shell tool
   in the `!` line is `curl` (present on every supported
   platform). The sidecar's endpoint takes form-encoded data
   (`--data-urlencode`) so values pass through untouched
   without JSON-escaping in shell.

2. **Orchestrator skills (Plan-23 amendment).** A SKILL.md
   with **no `!` exec line**. The body is prose that the
   agent executes in the live turn via the `Bash` and `Skill`
   tools (subject to `allowed-tools`). Today's only
   orchestrator is `/demo`; `/demo` chains other skills via
   the `Skill` tool to produce real
   `claude_code.skill_activated` events the collector records
   (`BR-DEMO-002` amended). A `!` exec line in an orchestrator
   would fire before the agent turn starts, defeating that
   purpose.

**Why:** the .NET-sidecar-as-boundary is the project's core
architectural commitment. A third language is exactly the
duplication / drift / test-surface bloat that boundary exists
to prevent. The orchestrator carve-out preserves the boundary
(orchestrators still go through the sidecar for the plan and
report — via `Bash(curl)` rather than `!` exec) while letting
the chained-skill execution path traverse the real Claude
Code harness.

Coverage: `BR-SKILL-007 (amended Plan-23) — orchestrator
skills have NO ! exec line` in `SkillPreconditionLintTests`.

### BR-DEMO-007 — `Skill(<name> *)` chain targets must be model-invocable (Plan-23)

A skill MAY only declare `Skill(<other> *)` in its
`allowed-tools` list when `<other>`'s SKILL.md frontmatter has
`disable-model-invocation: false`. Otherwise the chain is
unreachable: the harness refuses to load a skill into the
agent's tool surface when the flag is `true`, so a
`Skill`-tool invocation against it fails before any chained
work can run.

This rule applies in both directions of the dependency:
- A skill that wants to be a chain target must opt in
  (`disable-model-invocation: false`).
- A skill that wants to chain to another must verify the
  target is opted in before adding the `Skill(<name> *)`
  entry.

**Why:** silent unreachability. The `Skill`-tool architecture
introduced for `/demo` in Plan-23 surfaced this when `/otel`'s
existing `disable-model-invocation: true` flag (set before
the chain architecture existed) blocked `/demo`'s chain to
`/otel up`. The fix was to flip `/otel`, but the rule needs
to be deterministic going forward so future chain targets
don't repeat the failure mode.

Coverage: deterministic check addable to the
architecture-review gate (`/architecture-review`) — scans
every SKILL.md, parses `allowed-tools`, asserts each
`Skill(<name> *)` target's frontmatter has
`disable-model-invocation: false`. (Plan-23 ships the rule;
the deterministic check is a subsequent plan's `BR-DEMO-007`
test.)

### BR-SKILL-006 — Deterministic work uses the .NET sidecar

Skill helpers must NOT ask the LLM to perform deterministic work.
Deterministic operations (parsing, validation, scanning, slug
normalisation, config probing, git-status parsing) MUST call the
.NET helpers sidecar.

**Why:** reproducibility, cost, speed, audit, and security — see
README's "Single sidecar for deterministic work — pros and cons".

### BR-DEMO-002 — `/demo` chains via the Claude Code Skill tool (Plan-23 amended)

`/demo` MUST invoke every chained action via the Claude Code
**`Skill` tool** in the live agent turn. The dispatch endpoint
(`/skills/demo/dispatch`) MUST emit a `DEMO_PLAN v1` body
listing each step as a `STEP_INVOKE: number=… skill="…"
args="…" label="…" expect="…"` marker; the `/demo` SKILL.md
body iterates the markers and invokes each step via the `Skill`
tool. Each `Skill` invocation traverses the real Claude Code
harness, producing a `claude_code.skill_activated` event the
collector records — **that is the integration-test signal**
this rule guarantees.

`/demo` MUST NOT call:

- the collector control client (`ICollectorControlClient`) for
  any action — only `IsHealthyAsync` (a status probe) is
  permitted, and only inside the pre-flight section;
- vendor HTTP APIs (e.g. `wttr.in`) directly;
- the OTLP receiver (`:4318`) directly;
- another skill's dispatch endpoint via in-process HTTP
  loopback (the retired `ISkillDispatchClient` path) — that
  bypasses the Claude Code harness, emits no
  `claude_code.skill_activated` events, and produces a
  false-green integration test result. Plan-23 retired the
  loopback client.

Read-only observation steps that summarise the *result* of
upstream skill calls (e.g. counting JSONL records by ticket ID)
are permitted as direct file reads — they verify, they don't
act. They appear in the plan as `STEP_OBSERVE` markers.

This makes `/demo` simultaneously:

- a **demonstration** of skill chaining (every action step is a
  real `Skill`-tool invocation visible to the harness),
- the project's **full-stack integration test surface** —
  exercising the entire skill stack including parsing,
  validation, the chained skill's `!` exec, `allowed-tools`
  matching, and the collector contract.

**Why:** the pre-Plan-23 implementation chained via
`ISkillDispatchClient` HTTP loopback inside the .NET sidecar.
That bypassed the Claude Code harness completely — no
`claude_code.skill_activated` events fired, the chained skills'
`!` exec / `allowed-tools` / Claude-side rendering were never
exercised, and every `/demo` run since the rule landed produced
a false-green integration test result. The 2026-05-04 incident
log captures the discovery; this rule rewrites the contract so
chained calls actually traverse the harness.

Coverage: `BR-DEMO-002 (amended) — dispatch never invokes
downstream skills via in-process loopback (returns plan only)`
in `DemoDispatchEndpointTests`; `BR-DEMO-002 (amended) —
happy-path declares 12 invoke steps + 2 observe steps; chained
skills are otel/enrich/weather only` in `OtelDomainDemoTests`.

### BR-EXTEND-010 — Targets expose their guided demo via `IDemoTarget` (Plan-23 amended)

A domain SHOULD register an `IDemoTarget` alongside its
`IDomain` implementation. The contract was renamed from
`IDomainDemo` in Plan-23 because future plans extend it to
per-skill targets (`/demo enrich`, `/demo weather`, …) — a
target is "the thing the user types after `/demo`".

`IDemoTarget` exposes the **live plan section** of `/demo`
only. The platform-level pre-flight (sidecar reachable,
collector control, output dir, persistent file, OTLP port —
`STEP 00.x` rows) and the teardown section live in
`DemoDispatchEndpoint` because they are platform concerns, not
target ones.

Each `IDemoTarget` carries a **collection** of `DemoCase`
records. Exactly one case has `IsDefault = true`. Plan-23 ships
one default case per target; future plans add additional cases
(e.g. `enrichment-only`, `lifecycle`, `recovery-offer`). Names
match `^[a-z][a-z0-9-]*$` and are unique within a target.

Contract:

```csharp
public interface IDemoTarget
{
    string TargetName { get; }                  // matches /demo <target>
    string TargetKind { get; }                  // "domain" | "skill"
    IReadOnlyList<DemoCase> Demos { get; }      // 1..N cases, exactly one IsDefault
}

public sealed record DemoCase(
    string Name,
    string Description,
    bool IsDefault,
    Func<DemoContext, CancellationToken, Task<IReadOnlyList<DemoStepDescriptor>>> Plan);

public sealed record DemoContext(string SessionId);

public sealed record DemoStepDescriptor(
    int Number,
    string Skill,           // chained skill name, empty for Kind="observe"
    string Args,             // chained skill args
    string Label,            // human-readable
    string Expect = "",      // optional response substring for PASS check
    string Kind = "invoke",  // "invoke" | "observe"
    string ObserveTarget = ""); // for Kind="observe"
```

Discovery is via DI: the dispatch endpoint takes
`IEnumerable<IDemoTarget>` and selects the first whose
`TargetName` matches. The default case (or a named one passed
as `/demo <target> <demo>`) supplies the plan. Absence emits a
`DEMO_UNKNOWN v1` marker.

The `OtelDomainDemo` implementation registers the OTEL
domain's target with one default case `happy-path` whose plan
walks 14 steps (BR-DEMO-001): `/otel up` → 3× `/otel set` →
`/otel get` round-trip → 2× `/enrich` → 4× `/weather` → 2×
JSONL observation → `/otel down`. Each invoke step is
materialised by the SKILL.md body via the `Skill` tool
(`BR-DEMO-002` amended), producing a real
`claude_code.skill_activated` event.

**Why opt-in:** not every domain has a demonstrable workflow.
Forcing every `IDomain` to ship demo steps would couple the
contract to a use-case that may not apply (e.g. a future
information-only domain). Splitting `IDemoTarget` from
`IDomain` keeps each concern independent.

### BR-EXTEND-014 — Every registered domain MUST ship a demo covering every documented action (Plan-23)

Each `IDomain` MUST register at least one `IDemoTarget` whose
default `DemoCase` invokes — via the `Skill` tool — every
action listed in the domain's public skill surface (the union
of verbs across all skills declared in
`IDomain.GovernedGlobs`).

"Best-effort" exemptions are permitted for actions whose effect
requires an external system the demo can't safely touch (e.g.
cloud credentials, irreversible production writes, side-effects
that would page on-call). Each exemption is named in the
domain's `IDemoTarget` documentation with a one-line reason.

**Why:** the `/demo` surface is the project's only end-to-end
integration test that traverses the real Claude Code harness
(`BR-DEMO-001`). A domain whose actions aren't covered by a
demo isn't integration-tested. The pre-Plan-23 false-green
incident (2026-05-04) showed the cost: every chained step
skipped the harness via `ISkillDispatchClient` loopback, every
reported pass was meaningless, and an entire architectural
subsystem (`disable-model-invocation` interaction with skill
chaining) was never exercised.

**How to apply:** at `/extend-skills <newdomain>` Phase 1.5,
the architecture-review gate verifies that the new `IDomain`
registration has an accompanying `IDemoTarget` registration
covering every skill verb the domain declares. Missing
coverage blocks Phase 2 (Implement) until either the demo case
is filled in or the missing actions are named exemptions in
the target's docstring.

Coverage: integration test that iterates `IEnumerable<IDomain>`
and `IEnumerable<IDemoTarget>` from DI, parses each domain's
skill verbs from `GovernedGlobs`, and asserts every verb is
named by an invoke step in the matching target's default case
(modulo exemptions). Plan-23 ships the rule; the deterministic
check lands as part of an upcoming plan's Phase 4.

### BR-EXTEND-011 — Domain-scoped integration testing

When a change set is presented to CI (or to a local pre-commit
check), the integration-test scope is **the union of domains
whose `IDomain.GovernedGlobs` or `IDomain.PlanFiles.Directory`
intersects any changed path**. Cross-domain changes — paths
under `docs/cross-domain/`, the cross-domain BR file
(`docs/business-rules.md`), the cross-domain incident log
(`docs/process-incidents.md`), or the project-root `CLAUDE.md` —
trigger every registered domain's integration tests.

A path that doesn't match any domain's globs AND isn't recognised
as cross-domain is **conservatively treated as cross-domain**:
better to over-test than to miss a regression caused by a tool
or config change that the domain registry doesn't yet know
about.

**Why:** Plan-9 partitions plan files, playbooks, and (in time)
domain code into per-domain subtrees. The same partitioning lets
us scope integration testing — a change inside
`docs/otel/plans/` doesn't need to re-run kai-platform's tests,
and vice versa. This shortens the integration-test loop without
sacrificing correctness, because cross-domain artefacts still
trigger the full sweep.

**The scope resolver:** `DomainImpactScope.ResolveImpactedDomains(
IReadOnlyList<string> changedPaths)` returns
`DomainImpactScopeResult(ImpactedDomains, CrossDomainTriggered)`.
`ImpactedDomains` is sorted and distinct. The resolver is
deterministic, takes no I/O, and is safe to call from any host
(CI script, pre-commit hook, the `/extend-skills` flow).

**Caller responsibilities:**

- Provide changed paths as project-root-relative (typically from
  `git diff --name-only origin/main...HEAD`).
- Honour `CrossDomainTriggered=true` by running every domain's
  integration tests; do NOT silently fall back to a single
  domain's scope.
- When `ImpactedDomains` is empty (no paths or no matches), the
  caller MAY skip integration tests — the change set has no
  domain-touching paths.

**Test target:** `DomainImpactScopeTests` covers plan-dir match,
governed-glob match, cross-domain-prefix triggers, top-level
cross-domain-file triggers, the over-test fallback for unmatched
paths, multi-domain unioning, empty input, Windows-backslash
normalisation, and result-shape (sorted, distinct).

### BR-DEMO-004 — Demo runs produce a durable human-readable report

Every `/demo <domain>` invocation MUST write a markdown report to
`output/demo-reports/<UTC-timestamp>-<domain>.md` correlating each
step with the OTEL records it emitted. The console response gains
exactly one line at the end:
`Report saved to: <path>`. `--no-report` opts out (fast-loop
scenarios per `BR-PROCESS-007`).

Layout (`DEMO_REPORT v1` schema):

1. **Header** — UTC timestamp, session id, plan enrichment value
   (or "(no plan...)"), total elapsed, pre-flight pass count,
   live-step pass count.
2. **Pre-flight** — markdown table with rows from the dispatch's
   pre-flight section.
3. **Live demo steps** — one section per step:
   - `### STEP NN — <label> (<elapsed> ms) — PASS|FAIL`
   - `- <detail>`
   - **OTEL records produced** during this step's `[StartedAt,
     EndedAt]` window matching the demo's `session.id`. Records
     embedded as a fenced JSONL code block.
4. **Final summary** — `DEMO RESULT: x/y PASS`.
5. **Teardown** — same instructions the console emits.
6. **Appendix** — full session JSONL slice. Records that don't
   fall into any step's window land here (orphans count is shown
   in the section header).

The schema-version line `DEMO_REPORT v1` follows
`BR-PROCESS-013`; future schema changes increment the version.

`MarkdownDemoReportWriter` produces this layout. `JsonlSliceReader`
fetches records (per `BR-DEMO-003` file-share semantics).

**Why:** without correlation between steps and OTEL records, the
demo's evidence value is half-complete. The report makes every
demo run a shareable artefact and turns "a run from Tuesday" into
something a contributor can review without re-running.

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

### BR-DEMO-006 — `/demo` is model-invocable so it can serve its integration-test purpose

`/demo`'s frontmatter MUST set `disable-model-invocation: false`.
The skill serves two audiences per `BR-DEMO-001` — a new user
running it as an onboarding tour, and a contributor (or Claude
running on a contributor's behalf) using it as the project's
end-to-end integration test surface. The latter purpose is only
reachable when Claude can chain to `/demo` directly.

The HITL gate is preserved by `BR-DEMO-005`'s
`RECOVERY_AVAILABLE v1` pattern (every recovery action requires
explicit user "yes") and by the standard skill-tool pre-flight
that surfaces every chain step to the user. Model-invocability
is about reachability for integration-test runs, not about
removing the user from the loop.

**Why:** the dual-audience contract in `BR-DEMO-001` is
load-bearing for this project — `/demo` IS the integration test.
Locking it behind `disable-model-invocation: true` (the original
default) made the integration-test half of the contract
inaccessible to Claude-driven verification runs, which is
exactly what we need during `/extend-skills` Phase 4 testing,
during architecture reviews that want to probe the platform
end-to-end, and during routine validation after a sidecar
rebuild.

### BR-DEMO-005 — `/demo` and `/extend-skills` self-recover when the sidecar is down

When the deterministic-helpers sidecar (`:5050`) is not
responding to `/healthz` and the lifecycle CLI reports
`State: NotRunning` or `State: Zombie` (port free or held by a
stale PID file we own), `/demo` and `/extend-skills` MUST emit a
`RECOVERY_AVAILABLE v1` marker pointing at `/skill-bootstrap
start`:

```
RECOVERY_AVAILABLE v1: skill="skill-bootstrap" verb="start" reason="<short rationale>"
```

The user gives one explicit "yes" (HITL); the skill chains via
the `Skill` tool to `/skill-bootstrap start`; on success the
original skill re-invokes itself and continues. The
`allowed-tools` list of every skill that emits this marker MUST
carry the matching `Skill(skill-bootstrap start *)` entry as the
tightest prefix that lets the chain through (`BR-SKILL-009`).

When the lifecycle CLI reports `State: Conflict` (port `:5050`
held by a process we don't own), the marker is **suppressed**
and the user is shown the CLI's `Reason` field with no offer to
fix. `BR-SECURITY-003` forbids us recommending we stop a process
we don't own.

**Why:** `/demo` exists so a new user can experience the
platform end-to-end with one command. Handing them a chain of
prerequisite commands defeats that purpose. `BR-SKILL-014`
already specifies the offer-then-chain pattern; this rule
applies it to the two skills (`/demo`, `/extend-skills`) that
are the project's primary entry points and that share the
same `:5050` dispatch dependency. Tested by
`tests/HelpersSidecar.Tests/Demo/DemoPreflightRecoveryTests.cs`.

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

### BR-SKILL-012 — `/architecture-review` is purely qualitative judgement (Shape B)

The `/architecture-review` skill MUST NOT contain deterministic
per-commitment checks. The dispatch endpoint loads context
(CLAUDE.md, business-rules, recent plans, target body, the
resolved domain's `TrustedReferences`) and renders a structured
prompt with the `ARCHITECTURE_REVIEW v1` schema; **Claude (the
user's session) is the analyst**. The dispatch is the API.

Mechanical checks belong in lint test classes (the BR↔test
biconditional, prefix tightness, schema-shape validation,
`BR-PROCESS-009`'s gate over the plan-file decisions section)
and remain there. `/architecture-review` is exclusively
Claude-as-analyst.

**Why:** `BR-SKILL-006` carves deterministic work to the sidecar
and judgement work to the LLM. Architectural review is
judgement — the interesting questions ("does this invert a tier
ownership?", "does this introduce god-object behaviour?", "is
this name misleading?") are exactly the ones not encodable as
deterministic checks. Including deterministic checks alongside
qualitative review would create false confidence in shallow
greens. Two jobs, two places: lint catches mechanical drift;
this skill catches qualitative drift.

The skill body's only deterministic logic is post-processing
validation of Claude's response shape against the schema (every
BR in scope has a STATUS row; every EXTENDS has a paired
ARCHITECTURE_DECISION_REQUIRED block; CITED URLs come only from
the resolved domain's `TrustedReferences`). The architecture
review itself remains pure judgement.

### BR-SKILL-013 — Skills are self-assessable against the AI-fluency 4 D rubric

Every skill in `.claude/skills/<name>/SKILL.md` MUST be scorable
against Anthropic's 4 D AI-fluency framework: **Delegation,
Description, Discernment, Diligence**. Per-dimension score is
0/1/2 (Absent / Partial / Strong); per-skill total is out of 8.

The deterministic half of the rubric is the **test target** for
this rule (the biconditional applies):

**Delegation** (3 sub-checks, all must pass for Strong):
- `disable-model-invocation` is set explicitly (true OR false).
- `allowed-tools` uses the tightest viable prefix per
  `BR-SKILL-009` (no bare `Bash`, `Skill`, `Bash(curl *)`, or
  unscoped wildcards).
- The body explicitly carves deterministic vs judgement work.

**Description** (3 sub-checks):
- `description` field present and ≥ 50 characters.
- `argument-hint` field present.
- `allowed-tools` field present and non-empty.

**Discernment** (2 sub-checks):
- Body cites at least one `BR-<AREA>-NN` identifier.
- Body emits a schema-version marker matching `<NAME> v<N>`.

**Diligence** (2 sub-checks):
- Body indicates durable artefact production (curl-to-sidecar
  pattern, `output/` write, `report`, or `commit` keyword).
- Body names an inverse / undo path (`revert`, `discard`,
  `down`, `unset`, `undo`, `clear`, `stop`).

The judgement half of the rubric (does the description
disambiguate? does the body enable verification? does the
rollback path actually work?) is **NOT** automated — per
`BR-SKILL-006` / `BR-SKILL-012` it is left to Claude (the
in-session analyst) reading the rendered `AI_LEVEL_REPORT v1`.

**Why:** without a structured rubric, AI-fluency claims are
qualitative ("this skill feels good"). With one, every skill
gets a row in a typed report; weaknesses surface concretely
("`weather` lacks BR citations and a schema marker — body needs
a `WEATHER_REPORT v1` example output and a `BR-` reference").
The deterministic half is run by the sidecar; the judgement
half is the part the human + Claude pair are uniquely qualified
for. Structured discipline + judgement at the seam, not flat
all-or-nothing.

**Test target:** `AiLevelCheckerTests` (per-dimension fixtures);
`AiLevelDispatchEndpointTests` (HTTP shape); `SkillFileParserTests`
(parser tolerance); `AiLevelReportWriterTests` (`AI_LEVEL_REPORT v1`
schema rendering). Self-conformance: `/ai-level ai-level` MUST
score 8/8 (the rubric-applier passes its own rubric — verified
in commit `69d75dc`).

**Out of scope:** global scope (`~/.claude/skills/`) is
intentionally not supported in v1 per `BR-SECURITY-003`. The
rule applies to project-local skills; a future plan adds the
global scope behind a startup flag.

### BR-SKILL-014 — Pre-flight checks emit a structured RECOVERY_AVAILABLE marker

When a skill's pre-flight detects a down-state that another
named skill in this project can recover (e.g. `/demo`'s
collector-control probe fails AND the OTLP port is free →
`/otel up` would recover), the dispatch endpoint MUST emit
exactly one `RECOVERY_AVAILABLE v1:` marker line on its own line
in the response body, in this shape:

```
RECOVERY_AVAILABLE v1: skill="<name>" verb="<verb>" reason="<short rationale>"
```

The skill's body MUST instruct Claude to: (1) parse the marker,
(2) ask the user "invoke `/<skill> <verb>` to bring it up?",
(3) on confirmation invoke the named skill via the `Skill` tool,
(4) re-invoke the original skill after the recovery returns.
The marker triggers an **offer**, never a silent chain — the
user's confirmation is mandatory per `BR-SECURITY-003`'s spirit
(no destructive/state-changing action without explicit consent).

**The marker MUST NOT be emitted when:**

- The down-state is not auto-recoverable by any project skill
  (e.g. another non-project process holds the port — we never
  recommend stopping a process we don't own per `BR-SECURITY-003`).
- The user has not pre-confirmed the chain. The marker only
  *offers* — Claude's interpretation is what executes.
- The destination skill itself is unavailable (e.g. its
  `Skill` tool is blocked by `disable-model-invocation: true`,
  by `disableSkillShellExecution`, or by the user's permission
  settings). Better to say "open the holder yourself" than to
  emit a marker that points at a closed door.

**`allowed-tools` consequence (`BR-SKILL-009`):** any consuming
SKILL.md that interprets the marker MUST list the chained skill
explicitly with the tightest viable prefix (e.g.
`Skill(otel up *)`, `Skill(skill-bootstrap start *)`). Bare
`Skill` is forbidden — the consumer's allowed surface stays
narrow.

**Upstream-emitter coupling (`BR-SKILL-015` clause).** When the
project's collector binds a non-default port (per `BR-OTEL-007`'s
`CollectorOptions.CollectorOtlpPort`), the upstream OTEL emitter
that targets the collector — Claude Code itself — MUST also be
pointed at the new port; otherwise the data path silently
produces zero records. The rewriter detects and fixes this drift
by setting `OTEL_EXPORTER_OTLP_ENDPOINT` in
`.claude/settings.local.json`'s `env` block. This is the third
surface in `BR-SKILL-015`'s v1 enumeration:
(a) sidecar loopback URL in `allowed-tools` and `!` exec lines,
(b) docker-pattern presence in `allowed-tools` driven by mode,
(c) `OTEL_EXPORTER_OTLP_ENDPOINT` in `.claude/settings.local.json`
driven by the resolved collector URL. **Caveat:** env vars are
read by Claude Code at process startup; the rewriter writes the
file successfully, but the new endpoint takes effect only on the
next Claude Code session (the skill surfaces this to the user
explicitly).

**Schema-version discipline (`BR-PROCESS-013`):** the marker
itself is a schema (`RECOVERY_AVAILABLE v1`); the version is
embedded in the marker prefix. Future schema changes increment
the version (`v1` → `v2`) so older consumers can detect and
ignore newer markers gracefully.

**Test target:**
`DemoEmitsRecoveryAvailableMarkerTests` (producer: marker
emission per state); `grep` audit (consumer: every consuming
SKILL.md mentions `RECOVERY_AVAILABLE v1`).

**Why:** before this rule landed, skills named recovery actions
in their pre-flight output ("fix: /otel up") but never offered
to chain them. The user had to read the line, copy the next
command, type it, and re-run the original. The skills are
supposed to bring themselves online — name + offer + chain on
confirmation. The structured marker turns a free-text
instruction into a contract that producers and consumers can
both validate against.

**Defect of origin:** Plan-13 (2026-05-04). Same defect as
`BR-OTEL-007`'s defect of origin — the routine `/demo otel` run
named two recovery options without offering either.

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

### BR-PROCESS-015 — Every durable artefact is registered in IArtefactRegistry

Every durable artefact this project writes (or that the user
maintains by hand) MUST be registered as an `ArtefactSpec` in
`HelpersSidecar.Artefacts.ArtefactSpecs.All`. The biconditional
applies: a writer exists ⇔ a registry entry exists.

**The registry contract:**

- `Name` — stable string identifier, unique across the catalogue.
- `KeyTemplate` — the path or remote-key pattern with named
  segments (e.g. `<utc-ts>`, `<domain>`, `<scope>`).
- `Destinations` — list of `DestinationRef`s naming destinations
  by `IArtefactDestination.Name`. Empty for `UserEdited` artefacts.
- `SchemaName` + `SchemaVersion` — schema-versioning surface
  (`BR-PROCESS-013`).
- `Lifecycle` — `OneShot | AppendOnly | Replaced | RuntimeState
  | UserEdited`.
- `GitTracked` — whether the artefact is committed to the repo.
- `Producer` — fully-qualified .NET type name OR a human-readable
  note for non-.NET producers (collector, hand-edited files).
- `GoverningBR` — the BR that governs the artefact's contract.
- `Owner` — domain name (`"otel"`, `"cross-domain"`) or `null`
  for harness-level artefacts.
- `CostClass` — `Free | PerWrite | PerWriteAndStorage`.

**Programmatic vs UserEdited:**

- **Programmatic** producers MUST write through `IArtefactWriter`,
  never via direct `File.WriteAllText`. The writer renders the
  template, walks destinations, and surfaces per-destination
  failure results.
- **UserEdited** artefacts (BRs, retros, incidents, plans) are
  registered for visibility — `/domain-info <owner> artefacts`
  shows them — but `IArtefactWriter` refuses to write them
  (raises `InvalidOperationException`). The user edits them by
  hand; the registry catalogues their existence.

**Test target (the biconditional):**

- `ArtefactSpecsTests.Every_Programmatic_Producer_Resolves` —
  every spec whose `Producer` looks like a type name (no spaces,
  no parens) MUST resolve via `Type.GetType`.
- `ArtefactRegistryTests` — name uniqueness, lookup, filters.
- `ArtefactWriterTests` — template rendering + destination
  walking + failure-mode handling.
- `ArtefactSpecsTests.Catalogue_Schemas_Are_Registered` — every
  schema named in `BR-PROCESS-013`'s catalogue table has at
  least one registered spec.

**Why:** without a typed registry, "where does X get written?"
has no single source of truth, every new feature has to
re-decide its output convention, and cross-cutting queries
("what's gitignored?", "what's at v2?", "what does OTEL
produce?") devolve to grep over markdown. The registry is the
typed answer.

**Defect of origin:** Plan-9 surfaced the implicit convention
as fragile under multi-domain. Plan-10 shipped `/ai-level` with
an unregistered artefact as the deliberate forcing function.
Plan-11 introduces this rule and retrofits ten existing
producers to satisfy it.

### BR-SECURITY-004 — Remote artefact destinations require explicit two-level opt-in

A remote `IArtefactDestination` (S3, database, HTTP webhook,
message queue, anything that crosses the loopback boundary)
MUST be opt-in at TWO levels before it is wired:

1. **Destination level.** The destination itself is declared in
   `appsettings.json` under `Artefacts:Destinations` AND enabled
   by an explicit startup flag (`--enable-remote-destinations`).
   Default: every remote destination is unwired.
2. **Per-artefact level.** Even when a destination is enabled
   project-wide, each `ArtefactSpec` must explicitly include a
   `DestinationRef` naming it. Enabling S3 doesn't enable it for
   every artefact; enabling it for `demo-report` doesn't enable
   it for `runtime-pid`.

**Why:** the sidecar binds `127.0.0.1`. Reaching out to remote
services expands the trust boundary permanently — credentials,
network egress paths, throttling/cost concerns, schema-version
synchronisation across deployments. Two-level opt-in makes the
expansion deliberate at both the destination definition AND
each artefact's individual destination list.

**Plan-11 ships zero remote destinations.** The rule lands now
so the security firewall is in place before any future plan
proposes a remote destination. When that plan arrives:

- Add the destination class (e.g. `S3Destination`) under
  `src/HelpersSidecar/Artefacts/`.
- Register it as `IArtefactDestination` only when the startup
  flag is present.
- Update individual `ArtefactSpec`s to opt into it.
- Verify by `/domain-info <owner> artefacts` that the right
  artefacts show the new destination and the wrong ones don't.

**Test target:** `ArtefactSpecsTests.No_Remote_Destinations_Yet`
asserts every destination in every spec is `"local-fs"` in v1.
Future commits that add remote destinations MUST update this
test (or replace it with one that asserts the opt-in flag was
honoured).

**Why this BR ships before any remote destination:** the
project's previous BRs (`BR-SECURITY-001` port binding,
`BR-SECURITY-002` shell-exec policy, `BR-SECURITY-003` install
consent) follow the same shape — define the firewall before
the first instance that needs it, so the discipline is the
default rather than a retrofit.

### BR-CODE-004 — Stage/promote spawns override config via command-line, not file edit

When a long-running component (sidecar, collector, future
tier-managed components) is spawned in green/blue staging, the
green instance MUST receive every setting it needs to differ
from blue **as command-line config arguments** (`--Section:Key=Value`),
not by editing a separate `appsettings.Staging.json` or
copying-and-mutating the appsettings file in the staging
directory. ASP.NET Core's command-line configuration provider
overrides file-based providers; one staging port (or any other
staging-specific value) is one extra argv element on the spawn,
nothing more.

**Why:** stage/promote (`BR-PROCESS-011`) builds the green
binary to `bin/Staging/` and spawns it on a different port. The
appsettings.json that ships in the staging directory is a copy
of the production one — it carries the production port. If we
allowed green to bind whatever was in its appsettings, both
instances would race for the same port and stage would silently
fail (`HealthCheckFailed` after 30 s). Editing the staged
appsettings file post-build is brittle (re-runs of `dotnet build`
overwrite it, and the divergence between blue's and green's
appsettings makes the whole staging directory non-deterministic).
Command-line argv overrides are deterministic, scoped to the
single staged process, and don't touch file system state outside
the green PID's lifetime.

**Concretely:**

- `ComponentRegistry.Default` populates `StagingSpec.SpawnArgs`
  with the staging dll path AND `--Listener:Port=<StagingPort>`.
- Future tier-managed components added to the registry must
  follow the same pattern for any setting that needs to differ
  between blue and green.
- Tests assert the override is present in `SpawnArgs`
  (`ComponentRegistryTests.Green_Spawn_Overrides_ListenerPort_To_StagingPort`).

**Defect of origin:** Plan-7 staging implementation. The first
green-spawn attempt bound the appsettings-baked port (5050)
and collided with blue, yielding a `HealthCheckFailed` outcome
that looked like a build problem but was a configuration
problem. Captured during Plan-9 implementation and fixed in
the same flow.

## PROCESS

### BR-PROCESS-001 — Skill changes go through `/extend-skills`

Any change touching `.claude/skills/**`,
`src/HelpersSidecar/Endpoints/*DispatchEndpoint.cs`, or
`src/HelpersSidecar/Application/*Verb.cs` MUST be made via the
`/extend-skills` flow (plan → architecture-review → implement →
build → test, each phase gated by explicit user confirmation and
committed separately). Phase 1.5 (architecture-review) lands per
`BR-PROCESS-009`; Phase 2 (implement) does not proceed until the
plan-file's "Architecture review decisions" section resolves
every `ARCHITECTURE_DECISION_REQUIRED` block from the review.

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

3. Commit `c2aca79` (Plan-14, 2026-05-04) — flips
   `/skill-bootstrap`'s `disable-model-invocation` from `true` to
   `false` so future `/demo` and `/extend-skills` sessions can
   chain `/skill-bootstrap start` via the `Skill` tool per
   `BR-DEMO-005`. The change cannot govern itself: in the
   bootstrapping session the harness cached the old flag value
   so the chain still wasn't usable that session, but the commit
   establishes the new floor for every subsequent session.
   Plan-14's Phase 2 (`feat(otel)` commit) is the normal-path
   commit that updates `/demo`, `/extend-skills`, and `/enrich`
   SKILL.md files to consume the new floor.

4. Plan-23 (2026-05-04) — flips `/otel`'s
   `disable-model-invocation` from `true` to `false` so the
   Plan-23 `/demo` skill-tool chain can reach `/otel up`,
   `/otel set`, `/otel get`, and `/otel down` via the `Skill`
   tool. The pre-Plan-23 architecture had no model-invocable
   skill chain (every chain went through the retired
   `ISkillDispatchClient` loopback), so the flag was
   architecturally fine. Plan-23 introduces the chain shape
   that `BR-DEMO-007` makes mandatory; this commit is the
   bootstrap that makes the rule apply to `/otel`. Same shape
   as exception #3 — a flag flip that establishes a new floor
   for subsequent sessions; lives inside Plan-23's
   `feat(otel)` Phase 2 commit, not as a separate hand-rolled
   commit.

These exceptions share the same shape: the committed skill is
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

**Plan-9 amendment — playbook location:** the `/extend-skills`
flow's playbook now lives at `docs/<domain>/playbook.md` (e.g.
`docs/otel/playbook.md`), not `.claude/skills/extend-skills/playbook.md`.
The `/extend-skills` skill remains the dispatcher; the playbook
is the resolved domain's authoritative narrative artefact, owned
alongside the rest of the domain's docs and plans. SKILL.md
links to the OTEL playbook as the example; runtime resolution
uses the domain's `IDomain.PlaybookPath`. Resolution recorded
in `docs/otel/plans/The-OTEL-Plan-9-domain-localised-plan-files.md`'s
"Architecture review decisions" section as **Evolve**.

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

### BR-PROCESS-013 — Multi-step lifecycle events produce schema-versioned durable reports

Every multi-step lifecycle operation that a human might want to
review later MUST produce a markdown report at a documented path
with a versioned schema marker on a line directly under the title.

The schema catalogue is **append-only**. New schemas register by
adding a row below; existing schemas evolve by incrementing the
version (`v1` → `v2`) and keeping the older version parseable
during the transition window. Adding a schema does not require
amending this rule's text — the catalogue is the registry.

Currently named lifecycle events:

| Event              | Report path                                      | Schema version            |
|--------------------|--------------------------------------------------|---------------------------|
| Demo run           | `output/demo-reports/<ts>-<domain>.md`           | `DEMO_REPORT v1`          |
| Promote attempt    | `output/promote-reports/<ts>-<component>.md`     | `PROMOTE_REPORT v1`*      |
| Architecture review| `output/architecture-reviews/<ts>-<plan>.md`     | `ARCHITECTURE_REVIEW v1`* |
| Plans index        | `docs/INDEX.md`                                  | `PLAN_INDEX v1`           |
| AI-level scoring   | `output/ai-level/<ts>-<scope>.md`                | `AI_LEVEL_REPORT v1`      |
| Domain-info query  | (inline JSON; not written to disk)               | `DOMAIN_INFO v1`          |
| Weather output     | (inline text; not written to disk)               | `WEATHER_FREETEXT v1`     |

(*) Plan-7's `PROMOTE_REPORT` and Plan-6's persisted
`ARCHITECTURE_REVIEW` follow this same schema-version pattern
when their implementations land. The schema is embedded
verbatim in the prompt (Plan-6) or rendered by the writer
(Plans 7, 8, 10).

**Plan-10 amendment:** the catalogue gained `AI_LEVEL_REPORT v1`
and `PLAN_INDEX v1` (the latter retroactively named — the writer
already followed the pattern from Plan-9). The catalogue's
append-only discipline lets new schemas register without
amending this rule's narrative; only the table grows.

**Plan-11 amendment:** `IArtefactRegistry` is canonical for the
schema catalogue. The table above is a snapshot; the registry
(`ArtefactSpecs.All`) is the source of truth. Any future schema
registers via a new `ArtefactSpec` entry; this rule's text does
not need amending. `BR-PROCESS-015` enforces the biconditional —
a writer exists ⇔ a registry entry exists.

The schema-version line lets future schema changes increment
the version while keeping older reports parseable. Reports are
NOT edited in-place after creation; cleanup verbs may delete
them, but content is immutable from the writer's perspective.

This rule **names the pattern** Plans 6, 7, and 8 each
individually adopted. Per `BR-PROCESS-005`'s evidence rule, the
rule lands at the third concrete example (Plan-8) — not earlier.

**Why:** the project's audit trail was previously a mix of
commits, BR text, and `process-incidents.md` entries. Adding
durable per-event reports makes the *transient* events (a demo
run, a promote, a review) auditable too. Schema versioning
makes the audit trail durable across project generations.

### BR-PROCESS-011 — Long-running platform components support zero-downtime rebuilds via stage/promote/discard

Every tier-managed component that hosts user-facing skill traffic
(today: the helpers sidecar) MUST expose three lifecycle verbs
through its tier-owning skill:

- `stage` — build to a staging output directory; spawn a second
  instance ("green") on a separate port; verify health. Does
  NOT touch the running blue instance.
- `promote` — atomic swap. Verify green is healthy → snapshot
  blue → kill blue → copy staged binary → restart blue → verify
  → kill green. On any failure, leave blue in a recoverable
  state (sweep cleans the PID file).
- `discard` — kill green; leave blue running unchanged.

The component's `ComponentSpec` declares its `Staging` slot
(`StagingSpec`) carrying `StagingPort`, `StagingPath`,
`StagingPidFile`, build command + args, and spawn command + args.
Two PID files (`<name>.pid` + `<name>-green.pid`) keep state
isolation.

Promote refuses to proceed if green is unhealthy; the user must
`discard` and `stage` again.

**Why:** the OTEL-continuity gap during rebuilds is a contract
violation under `BR-EXTEND-009` (plan-tagging) — telemetry
continuity is now load-bearing. Stage/promote brings the gap
from seconds (current stop-build-restart) down to milliseconds
(atomic swap).

### BR-PROCESS-012 — Promote operations are atomic with rollback on failure

During promote:

1. Verify green is healthy (else refuse).
2. Snapshot the existing blue binary directory to
   `<binary-dir>.bak/`.
3. Kill blue → copy staged binary → restart blue → verify
   blue's `/healthz`.
4. **On verify-fail:** restore the snapshot, restart blue from
   it, leave green running so the user can inspect what went
   wrong. Return `RolledBack`.
5. **On verify-pass:** kill green, delete green PID file, return
   `Promoted`.

The state machine MUST never end in "no blue running and no
green running" except through explicit user `discard + stop`.
Any internal failure path leaves at least one viable instance.

The snapshot directory persists between promotes (it's
overwritten on the next stage's promote attempt; gitignored).
The user inspects it for diagnostics if a failed promote raises
a question about what was running before.

**Why:** a half-failed promote that leaves the system off is
worse than the current rebuild gap. Rollback-on-failure is the
contract that makes promote safer than the current
stop-build-restart pattern.

### BR-PROCESS-009 — Architecture evolution requires explicit human decision

When `/architecture-review` (Plan-6 / Shape B) reports any
commitment with `STATUS: EXTENDS`, the `/extend-skills` flow
MUST gate Phase 2 (Implement) on a recorded human decision per
the four resolution words:

- **Evolve** — amend the affected `BR-*` text (and any
  consequent CLAUDE.md sections); the plan extends the
  architecture intentionally.
- **Constrain** — rework the plan to stay within current
  commitments; re-run `/architecture-review`.
- **Defer** — capture the question as an open architectural
  item; the change does not land in this plan.
- **Override** — accept the deviation as a one-off with a
  one-line justification recorded in the plan; useful for
  deliberate one-offs that don't justify a rule change.

**Invocation vs. decision recording — the gate is at the
decision, not at the invocation.** `/architecture-review` MAY be
invoked automatically by `/extend-skills` Phase 1.5 (chained via
the `Skill` tool); the *report* it produces is the **input** the
human reads to decide. The HITL gate is the resolution step —
the user types one of the four resolution words per
`ARCHITECTURE_DECISION_REQUIRED` block. Generating the report
without Claude triggering it would defeat the report's purpose:
part of the report's job is to identify which decisions Claude
can make versus which require human judgement, so producing it
*is* part of the decision-making tree — not separate from it.

The decision lands in the plan file's "## Architecture review
decisions" section. The deterministic gate
(`/helpers/plans/architecture-review-gate`) verifies that every
`ARCHITECTURE_DECISION_REQUIRED` block in the plan body has a
matching resolution line in that section before Phase 2 can
proceed.

This rule builds on `BR-PROCESS-005` (flag architectural
decisions; document deviations) and `BR-PROCESS-006` (≥ 3
perspectives). `BR-PROCESS-009` adds the *enforcement gate* —
the EXTENDS marker must be resolved by a human, not silently
incorporated.

**Why:** today architecture evolution can happen by accident — a
plan introduces a pattern that contradicts a BR, and unless the
reviewer notices, the contradiction lands. The architecture
agent makes the contradiction visible; this rule makes the
resolution explicit. The deterministic gate makes "yes I
resolved it" auditable. Distinguishing invocation from
decision-recording lets the chain run end-to-end without the
user having to type the slash command, while preserving the gate
that actually matters (the resolution).

**Defect of origin:** Plan-13 Phase 1.5 (2026-05-04). The
playbook expected `/extend-skills` to chain `/architecture-review`,
but `BR-SKILL-002`'s literal text required `disable-model-
invocation: true`, blocking the chain. The amendment to
`BR-SKILL-002` (read-only skills exempt) plus this clarification
(invocation may chain; decision recording is the gate) closes
the gap.

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

### BR-PROCESS-014 — Action plans are sequenced, parallelised, and never silently skipped

When a flow's retro, post-mortem, architecture review, or any
other structured analysis produces an action list, **every item
on that list MUST land in one of two places** before the session
or PR closes:

1. **Actioned** — the work happens, with a commit that names the
   item.
2. **Recorded as deferred-with-rationale** — an entry in
   `docs/retros.md` (or the relevant plan file's "Out of scope"
   section) names the item, why it's deferred, and what would
   trigger picking it up. "Deferred" is the only legitimate
   alternative to action; **"skip" is not a category**.

Items MUST be sequenced in the smallest viable dependency order:
the items with no upstream prerequisite go first. Items that have
no dependency on each other SHOULD be batched and executed in
parallel — multiple tool calls in one message, multiple
independent commits in any order, or one composite commit when
the items are tightly coupled.

The actor handling the action plan MUST surface the dependency
graph and the parallelisation choice **before** starting. The
user can redirect; silent re-ordering is itself a process miss.

**Concretely:**

- A retro produces 7 items. The actor sequences them into waves
  (Wave A: independent docs; Wave B: code that depends on
  registrations; Wave C: live-system verification). Wave A's
  items are batched; Wave B's items are sequential because each
  changes the build/test surface; Wave C is read-only and
  closes the loop.
- An architecture review surfaces an OUT-OF-SCOPE concern.
  Either it gets actioned in the same flow (commit), or it lands
  in `retros.md` as deferred-with-rationale (also a commit). It
  cannot be silently dropped.
- Items the user explicitly tells the actor to skip MUST still
  be recorded as deferred-with-rationale ("user declined this
  iteration: <reason if given>") so the next reviewer can see
  the choice was made deliberately.

**Why:** action lists that drift become technical debt with no
owner and no audit trail. The forcing function is small (two
lines in a markdown file) but the failure mode it prevents is
large: "we'll get to it" → "where did that idea go?" → silent
loss of the loop's learnings. This rule makes the cost of NOT
deferring explicit (a recorded entry) so that real action wins
by default.

**Test target:** there is no automated test of "the actor obeyed
this rule" — the rule is procedural. Compliance is verified by
spot-checking retros and PRs: if an action plan was produced,
every item must appear in the resulting commits OR in
`retros.md`'s deferral log. A reviewer who finds an unactioned,
unrecorded item from a recent retro flags a process incident.

The rule itself exists as the consequence — the documented
discipline. If a future contributor finds this rule unhelpful
and proposes demoting it, they MUST follow the same procedure:
record the demotion proposal in `retros.md`, action it through
the evidence machinery (`BR-PROCESS-003`), and either land the
demotion or carry the proposal as deferred. No silent demotion.

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
