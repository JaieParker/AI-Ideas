# Go OTel collector via OCB with custom enrichmentprocessor + enrichmentctlextension

> Plan-2 file produced by `/otel-extend` Phase 1 (followed
> directly by the developer per `BR-PROCESS-001`'s playbook).
> Commit prefix: `plan:`. Subsequent phases follow the canonical
> prefixes documented in `.claude/skills/otel-extend/commit-prefixes.md`.

## Motivation

Path A was chosen after a multi-perspective trade-off (engineering
/ operations / strategy) recorded in `cbb5630` and earlier
commits. Path A keeps full contrib-ecosystem access by building a
real OTel Collector via OCB; the only custom Go we author is two
small modules that together implement runtime-mutable per-session
enrichment, which no stock contrib component currently provides
([verified against the contrib processor index and the
lookupprocessor README](docs/process-incidents.md), and tracked in
upstream issue [#41816](https://github.com/open-telemetry/opentelemetry-collector-contrib/issues/41816)).

This plan delivers the missing collector binary and unblocks every
skill verb that's been gracefully degrading to "collector control
API not reachable" in the prototype demo.

## Files affected

| Path | Change |
|---|---|
| `components/enrichmentctlextension/go.mod` | NEW — Go module, pinned to `go 1.23.0` (matches OCB v0.129.0 minimum) |
| `components/enrichmentctlextension/factory.go` | NEW — `extension.NewFactory()` + `Config` struct + `createExtension()` |
| `components/enrichmentctlextension/extension.go` | NEW — Start/Shutdown lifecycle. HTTP server on `:13133`. State held as `atomic.Pointer[*State]` to immutable maps (BR-ENRICH-012). Endpoints: `/sessions/{id}/enrichments` GET/POST; `/sessions/{id}/collection` GET/POST; `/persistent-enrichments` GET/POST; `/persistent-enrichments/{key}` GET; `/persistent-enrichments?keys=a,b` GET (multi-key). Persistence: load `persistent-enrichments.json` at startup; `fsnotify` watcher reloads on disk change. |
| `components/enrichmentctlextension/extension_test.go` | NEW — race-detector tests covering atomic-swap correctness (BR-ENRICH-012), persistence load/reload, HTTP control API contract |
| `components/enrichmentprocessor/go.mod` | NEW — Go module |
| `components/enrichmentprocessor/factory.go` | NEW — `processor.NewFactory()` for traces/logs/metrics; `Config` references the extension by name |
| `components/enrichmentprocessor/processor.go` | NEW — `ConsumeTraces` / `ConsumeLogs` / `ConsumeMetrics`. For each record: extract `session.id` → look up session map and persistent map via the extension's `State` snapshot → if collection is disabled for the session, drop the batch (BR-ENRICH-004); otherwise stamp persistent attributes first, then per-session (BR-ENRICH-008). |
| `components/enrichmentprocessor/processor_test.go` | NEW — tests for stamping order, drop-on-disabled, missing-session-id pass-through, value-overwrite vs append |
| `manifest.yaml` | NEW — OCB manifest pinning v0.129.0 of stock components (`otlpreceiver`, `batchprocessor`, `fileexporter`, `otlphttpexporter`, `healthcheckextension`) plus our two custom modules via `gomod` + `path` |
| `config.yaml` | NEW — Collector config wiring `enrichmentctl` extension and `enrichment` processor into traces / logs / metrics pipelines. File output by default; `otlphttp` exporter configured but not in any pipeline initially (opt-in by editing) |
| `tests/integration/collector_smoke.sh` | NEW — synthetic OTLP request driver that asserts the JSONL output contains expected enrichments |
| `dist/windows-amd64/claude-otel-collector.exe` | NEW — built binary (lands in Phase 3) |

## Behavioural change

**Before:** The collector binary doesn't exist. Skill verbs that
touch the collector (`/otel set` / `/otel on|off` / `/enrich`) all
return "collector control API not reachable on 127.0.0.1:13133".
OTLP from Claude Code goes nowhere; `output/telemetry.jsonl`
doesn't exist.

**After:**

1. Run `./dist/windows-amd64/claude-otel-collector.exe --config=config.yaml`.
2. Claude Code's OTLP exporter (configured to `127.0.0.1:4318`)
   delivers traces/logs/metrics to the collector.
3. `enrichmentprocessor` stamps each record with the per-session
   map and persistent map held by `enrichmentctlextension`.
4. `fileexporter` writes OTLP/JSONL to `output/telemetry.jsonl`.
5. Skill verbs hit the control API on `127.0.0.1:13133`. State
   mutates in-process atomically; the next OTLP batch picks up
   the change.

The full 15-step demo (including the JA-0001 → JA-0002 transition)
becomes observable in the JSONL: every line carries the persistent
attributes (user/workstation/version) plus the session's current
ticket-reference, and the cutover between values is visible in
record timestamps.

## Test approach

Three layers, ordered from fastest to most realistic:

1. **Unit (`go test ./components/... -race`)** — covers value
   object behaviour, factory creation, atomic-swap correctness
   (BR-ENRICH-012), processor stamping order (BR-ENRICH-008),
   collection-disabled drop (BR-ENRICH-004). Sub-second.
2. **Integration (`tests/integration/collector_smoke.sh`)** —
   starts the collector with a test config, sends synthetic OTLP
   requests via `curl`, asserts the JSONL contents. Covers the
   full BR-ENRICH-001..010 + BR-ENRICH-012 end-to-end against a
   real binary.
3. **Manual end-to-end** — replay the 15-step demo we prototyped
   earlier with both the .NET helpers sidecar and the Go
   collector running. Confirm JA-0001 vs JA-0002 transition
   visible in `output/telemetry.jsonl`.

CI runs layers 1 and 2 on the project's existing CI surface
(currently absent — task left to the matrix-CI sub-task we deferred).

## Rollback steps

Each phase commits separately so each is independently revertable:

1. `git revert <plan-commit-sha>` — drops Plan-2 alone.
2. `git revert <feat-commit-sha>` — drops the Go components,
   manifest, and config (Phase 2).
3. `git revert <chore-commit-sha>` — drops the rebuilt binary
   (Phase 3).
4. `git revert <test-commit-sha>` — drops integration tests
   (Phase 4).

The plan commit's SHA gets filled in once it lands; subsequent
phase SHAs are filled in as they're created and echoed back to
the user.

## Out of scope

- **gRPC OTLP receiver.** HTTP/protobuf only in v1; gRPC needs
  a config-flag flip and dependencies but is unused by Claude
  Code today.
- **TLS / authentication** on receiver and control API
  (localhost-only per BR-OTEL-001 and BR-HELPERS-002).
- **File rotation** on the JSONL output. Stock `fileexporter`
  has it; we don't enable it in v1 to keep the demo's logs
  cumulative.
- **Atomic deletes / safe writes for `persistent-enrichments.json`.**
  v1 uses a simple `os.WriteFile` with `O_TRUNC`. A v2 hardening
  would write to a tmpfile + atomic-rename.
- **Multi-platform binaries.** v1 builds `windows-amd64` only
  (your platform). Cross-platform binaries land in a follow-up
  via `goreleaser` or per-platform OCB invocations.
- **End-to-end CI matrix.** v1 verifies locally; CI matrix is
  deferred (no CI surface configured yet).
