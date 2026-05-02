# Live OTEL enrichment for Claude Code

> **TL;DR** — Tag every span/log/metric Claude Code emits with
> work-item context — ticket, feature, experiment — per-session or
> persistent across sessions. Two slash commands: `/otel` (setup,
> on/off, persistent config, self-extend) and `/enrich` (per-session
> attributes). Built on the official OpenTelemetry Collector via
> OCB plus a small .NET deterministic-helpers sidecar with OpenAPI.
> File output by default; any OTLP backend by editing one config
> line. Clone, open Claude Code in the dir, type `/otel` — done.

Tag every span, log, and metric Claude Code emits with the
work-item context you actually care about — a ticket, a feature,
an experiment — and have it stick across the whole session.
Multiple Claude windows stay isolated; switching tickets mid-flight
takes one slash command. Output lands in a local file by default;
forwarding to any OTLP backend is a one-line config change.

The full design is in [`The-OTEL-Plan.md`](./The-OTEL-Plan.md).
Project rules loaded into every Claude Code session here are in
[`CLAUDE.md`](./CLAUDE.md).

## Quickstart

```text
# 1. Clone (or download a release tarball)
git clone <repo> && cd OTEL

# 2. Open Claude Code in this directory
claude

# 3. Inside Claude
/otel                              # bootstrap + start the collector
/otel set team:platform            # persistent enrichment (every session)
/enrich ticket.id PROJ-1234        # per-session enrichment
                                   #   ask Claude anything; telemetry is tagged
```

Output appears in `./output/telemetry.jsonl`. Tail it with
`Get-Content output/telemetry.jsonl -Wait` (PowerShell) or
`tail -f output/telemetry.jsonl` (bash).

## Commands

Canonical list. Mirrored verbatim from
`.claude/skills/otel/HELP.md` (which `/otel help` prints).

```text
# /otel — bootstrap, master switch, persistent config
/otel                              setup-and-start; idempotent
/otel on                           collection enabled (this session)
/otel off                          collection paused (this session)
/otel status                       what's running, what's bound
/otel restart                      restart the collector binary
/otel help                         print this list
/otel set <key>:<value>            persistent enrichment (every session)
/otel get <key>                    read one persistent value (404 if unset)
/otel get <key1> <key2> ...        read several at once; always 200 + array
/otel unset <key>                  remove one persistent enrichment
/otel config                       show the persistent map
/otel config clear                 wipe the persistent map (confirms)
/otel extend [<topic>]             chain to /otel-extend (self-modify)

# /enrich — per-session enrichments (in-memory only)
/enrich <key> <value>              set
/enrich --remove <key>             remove one
/enrich --clear                    drop all
/enrich --show                     list current

# /weather — example skill, demonstrates the pattern
/weather                           current weather, IP-located
/weather <place>                   current weather for <place>
```

## About the OpenTelemetry Collector we use

This project builds on the **official OpenTelemetry Collector** —
the upstream Go distribution maintained at
[`open-telemetry/opentelemetry-collector`](https://github.com/open-telemetry/opentelemetry-collector)
and the components in
[`open-telemetry/opentelemetry-collector-contrib`](https://github.com/open-telemetry/opentelemetry-collector-contrib).
We don't fork it, we don't reimplement it. We assemble a custom
distribution from it using the **OpenTelemetry Collector Builder**
([`ocb`](https://github.com/open-telemetry/opentelemetry-collector/tree/main/cmd/builder)),
adding two small Go modules of our own.

**Why this is the right base:**

- **Vendor-neutral and standards-blessed.** It's the reference
  implementation of OTLP, the wire protocol every observability
  vendor now speaks. Anything we ship will work with Honeycomb,
  Datadog, Tempo, Jaeger, Loki, ClickHouse, Splunk, Dynatrace,
  Prometheus, and dozens more — without code changes, only config
  changes.
- **Battle-tested.** The Collector runs in production at huge
  scale across the OTel ecosystem. We inherit that hardening for
  free.
- **Component ecosystem.** Hundreds of receivers, processors, and
  exporters are already written, reviewed, and maintained. Our
  needs (OTLP in, file out, OTLP out, batching, attributes,
  transform, healthcheck) are entirely covered by stock
  components — see the table in
  [`The-OTEL-Plan.md`](./The-OTEL-Plan.md#whats-standard-what-we-wrote).
- **Standard configuration.** Anyone who's ever read an OTel
  Collector `config.yaml` can read ours. Receivers, processors,
  exporters, pipelines — same shape, same semantics. No project-
  specific DSL to learn.
- **OTTL.** The Transformation Language gives us a powerful,
  declarative way to manipulate telemetry that we couldn't justify
  building ourselves.
- **Active maintenance.** The CNCF backs it; releases ship every
  few weeks; security issues get triaged fast.

**What we add.** Two Go modules, ~300 lines combined:

- `enrichmentprocessor` — runtime-mutable cousin of the
  `attributesprocessor`, stamping each record with per-session
  and persistent attributes.
- `enrichmentctlextension` — exposes the HTTP control API
  (`/sessions/{id}/enrichments`, `/persistent-enrichments`,
  `/sessions/{id}/collection`) and owns the in-memory state the
  processor reads.

These two modules are listed in `manifest.yaml` alongside the
stock components. `ocb` builds the binary; `config.yaml` wires
everything up using the standard pipeline syntax.

## Minimising the collector to your stack

The OTel Collector's `contrib` distribution ships every component
imaginable — Prometheus, Kafka, AWS, GCP, Azure, Jaeger, Zipkin,
Splunk HEC, dozens of cloud-vendor receivers and exporters. That
makes it large (~150 MB) and broadens the surface area beyond what
any single project actually uses.

**OCB lets you ship only what you use.** The manifest you hand to
`ocb` is a list of receiver / processor / exporter / extension
modules; anything not listed is not compiled in. The result:

- A binary that's a fraction of contrib's size — typically tens of
  MB instead of hundreds.
- A smaller attack surface (fewer components → fewer CVEs to track).
- Faster startup (no unused factories to register).
- A clearer security review story (you can name every component in
  the binary).

**Our manifest is already minimised** to the components this
project actually needs:

```yaml
# manifest.yaml (excerpt)
receivers:
  - gomod: go.opentelemetry.io/collector/receiver/otlpreceiver vX.Y.Z

processors:
  - gomod: go.opentelemetry.io/collector/processor/batchprocessor vX.Y.Z
  - gomod: github.com/open-telemetry/opentelemetry-collector-contrib/processor/attributesprocessor vX.Y.Z
  - gomod: github.com/open-telemetry/opentelemetry-collector-contrib/processor/transformprocessor vX.Y.Z
  - gomod: ./components/enrichmentprocessor   # ours

exporters:
  - gomod: github.com/open-telemetry/opentelemetry-collector-contrib/exporter/fileexporter vX.Y.Z
  - gomod: go.opentelemetry.io/collector/exporter/otlphttpexporter vX.Y.Z

extensions:
  - gomod: github.com/open-telemetry/opentelemetry-collector-contrib/extension/healthcheckextension vX.Y.Z
  - gomod: ./components/enrichmentctlextension  # ours
```

**To trim further** for your own deployment:

1. Open your `config.yaml` and list every component name actually
   referenced under `receivers:`, `processors:`, `exporters:`, and
   `extensions:`. (For example, you may not be forwarding anywhere,
   so `otlphttpexporter` is unused.)
2. Edit `manifest.yaml` to delete the matching `gomod` entries.
3. Rebuild: `ocb --config=manifest.yaml`. You'll get a binary
   smaller than the one we ship, with only the components you
   need.
4. Commit the manifest change so reviewers see the smaller surface.

The same logic works in reverse: if you need a backend we don't
ship for (Kafka, S3, Loki), add the `gomod` line to the manifest,
rebuild, and update `config.yaml`. No fork, no patches.

## Single sidecar for deterministic work — pros and cons

The .NET helpers sidecar exists to enforce a rule: **AI does
non-deterministic work; the sidecar does deterministic work.**
A skill helper never asks Claude to slugify a string, scan a
directory, validate a regex, or read git status, because none of
those have an opinion. They have an answer.

This is a deliberate trade-off. Honest pros and cons:

**Pros**

- **Reproducible.** Same input, same output, every time. The
  business rules in `docs/business-rules.md` can be tested
  exhaustively; every endpoint has at least one passing test.
- **Cheap.** A localhost HTTP call doesn't burn LLM tokens. Over a
  long session that's real money saved and real context window
  preserved for actual reasoning.
- **Fast.** Single-digit-millisecond round-trips vs. hundreds of
  milliseconds (or more) for an LLM call.
- **Auditable.** Every operation has source you can read and an
  OpenAPI contract you can grep. AI behaviour is opaque by
  comparison and shifts between model versions.
- **Hard to prompt-inject.** Deterministic code is bounded by its
  source. An LLM doing the same job is one carefully-crafted input
  away from misbehaving.
- **Versionable.** Pin the sidecar version, pin the behaviour. Pin
  a model and the behaviour can still drift on the next minor
  release.
- **Offline-capable.** No model-provider round-trip; the demo runs
  on a plane.
- **Clear boundary.** Code reviewers know exactly which lines are
  "AI judgement" and which are "code logic". The latter is a much
  bigger fraction than people expect.

**Cons**

- **Up-front cost.** You have to write and maintain the sidecar.
  Asking Claude to do the same job is zero-effort the first time.
- **Single point of failure.** If the sidecar crashes, skills lose
  their deterministic capabilities. (Mitigation: the binary
  starts on `/otel` and `/healthz` is monitored; failure is loud
  and recoverable.)
- **Schema rigidity.** OpenAPI commits you to shapes. Adding a new
  deterministic capability is a design step, not a free prompt
  edit.
- **Distribution overhead.** A .NET binary per platform has to
  ship; the skill is no longer "just a markdown file". (Mitigation:
  pre-built binaries committed to `dist/`, and the skill ships its
  helper script alongside.)
- **Two languages to maintain.** Go for the collector, .NET for the
  helpers, Node for skill glue. Each contributor needs to tolerate
  reading three syntaxes.
- **Lock-step versioning.** Sidecar API and skill helpers must
  stay in sync. Protocol changes need a migration plan.
- **Latency floor for trivial work.** Slugifying a string through
  HTTP is more overhead than inlining a regex in Node. (Mitigation:
  the latency is still under a millisecond locally; ergonomics win.)
- **Discovery friction.** Adding "do X but slightly different" via
  the LLM is a one-line prompt. Adding it via the sidecar is a
  build-test-rebuild cycle.

**Our stance.** We accept the cons. The reproducibility, cost,
audit, and security wins compound across thousands of skill
invocations; the cons are visible only when you're adding new
capabilities, which is a rare event. The boundary is the point —
you should always know which side of it any given operation lives
on. If you find yourself prompting Claude to do something that has
a single correct answer, that's the signal to add an endpoint.

## Architecture in one paragraph

Two local services on `127.0.0.1`. The **Custom OTel Collector**
(the one we just talked about) handles OTLP receive → enrichment →
export, listening on `:4318` for OTLP/HTTP and exposing a tiny
control API on `:13133`. The **.NET 10 deterministic-helpers
sidecar** on `:5050` hosts every deterministic operation the skills
need — plan-file scanning, slug normalisation, argument validation,
config probing — with an OpenAPI spec at `/openapi.json`. The
skills' Node helpers are thin HTTP clients of one or both services
and contain no business logic. Full diagram and component breakdown
in [`The-OTEL-Plan.md`](./The-OTEL-Plan.md#architecture).

## Where to read more

- [`The-OTEL-Plan.md`](./The-OTEL-Plan.md) — full design,
  capability matrix, threat model, business rules.
- [`CLAUDE.md`](./CLAUDE.md) — rules every change must follow
  (loaded into Claude Code automatically when you open this dir).
- `http://127.0.0.1:5050/openapi.json` — live OpenAPI spec when
  the helpers sidecar is running. A static copy is also at
  `src/HelpersSidecar/openapi.json` for offline reading.

## Standards

- [OpenTelemetry Collector docs](https://opentelemetry.io/docs/collector/)
- [Building a custom collector with OCB](https://github.com/open-telemetry/opentelemetry-collector/tree/main/cmd/builder)
- [Claude Code monitoring (OTEL emission)](https://code.claude.com/docs/en/monitoring-usage)
- [Claude Code skills / slash commands](https://code.claude.com/docs/en/slash-commands)

## Licence

TBD — pick before first public release.
