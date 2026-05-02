# Live OTEL enrichment for Claude Code

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

## Architecture in one paragraph

Two local services on `127.0.0.1`. The **Custom OTel Collector**
(the one we just talked about) handles OTLP receive → enrichment →
export, listening on `:4318` for OTLP/HTTP and exposing a tiny
control API on `:13133`. The **.NET 8 deterministic-helpers
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
