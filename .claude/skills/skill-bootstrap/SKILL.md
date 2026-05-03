---
name: skill-bootstrap
description: Bootstrap and lifecycle for the .NET deterministic-helpers sidecar — the platform every other skill in this project depends on. Probes pre-requirements (.NET 10 SDK, sidecar source present, sidecar built, port 5050 free or owned by sidecar, healthz reachable) and prints a structured PASS/FAIL table. Verbs - no arg (status only, read-only), install (dotnet build), start (sweep zombies + spawn sidecar in background + poll healthz), stop (graceful shutdown of the listener on :5050), stage (build to bin/Staging/ + spawn green on :5051), promote (atomic blue↔green swap with bin/Debug.bak/ rollback), discard (kill green, leave blue). OTEL-independent - this is about the platform, not the Go collector. The collector's lifecycle is owned by /otel up / /otel down. Owns sidecar zombies per BR-PROCESS-008. Stage/promote/discard per BR-PROCESS-011 / 012.
argument-hint: [install | start | stop | stage | promote | discard] (no arg = status table only)
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/healthz *) Bash(curl http://127.0.0.1:5051/healthz *) Bash(dotnet --version) Bash(dotnet --list-sdks) Bash(dotnet build src/HelpersSidecar/HelpersSidecar.csproj *) Bash(dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll *) Bash(dotnet src/HelpersSidecar/bin/Staging/net10.0/HelpersSidecar.dll *) PowerShell(Get-NetTCPConnection *) PowerShell(Stop-Process *) Read Write Glob
---

!`curl http://127.0.0.1:5050/healthz -sS --max-time 2 || printf 'SIDECAR_DOWN\n'`

You are running `/skill-bootstrap` — the bootstrap heart of the deterministic-helpers sidecar. This is the only skill whose `!` preprocessing line does NOT dispatch through `:5050/skills/<name>/dispatch`, because its job is to tell the user when `:5050` is not there. Every other skill in this project routes through that sidecar; without `/skill-bootstrap`, nothing else works.

The probe above produced one of two outputs:

- A JSON body like `{"status":"ok","uptime_s":42,"version":"1.0.0"}` — the sidecar is up.
- The literal string `SIDECAR_DOWN` — the sidecar is not running (or the probe timed out).

## Step 1 — print the pre-requirement table

Run the five probes below (in order) and print exactly this table, with `PASS` or `FAIL` per row and a short detail/fix string:

1. **.NET 10 SDK present** — run `dotnet --version` via `Bash`. PASS if exit 0 and the major version is `≥ 10`. On FAIL, detail = `"missing — install from https://dotnet.microsoft.com/download/dotnet/10.0"`.
2. **Sidecar source present** — `Glob` for `src/HelpersSidecar/HelpersSidecar.csproj`. PASS if found.
3. **Sidecar built** — `Glob` for `src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll`. PASS if found; on FAIL detail = `"run /skill-bootstrap install"`.
4. **Port 5050 free or owned by sidecar** — if the `!` probe above returned JSON, PASS (the sidecar owns the port). Otherwise run `PowerShell` `Get-NetTCPConnection -LocalPort 5050 -State Listen -ErrorAction SilentlyContinue`. PASS if no result (free); on FAIL detail = `"port held by another process (pid <N>)"`.
5. **Sidecar healthz** — PASS if the `!` probe returned a JSON object with `"status":"ok"`. On FAIL detail = `"run /skill-bootstrap start"`.

Format:

```
PRE-REQUIREMENT                    STATUS  DETAIL
1. .NET 10 SDK present              PASS    10.0.x
2. Sidecar source present           PASS
3. Sidecar built                    FAIL    run /skill-bootstrap install
4. Port 5050 available              PASS    free
5. Sidecar healthz                  FAIL    run /skill-bootstrap start
```

## Step 2 — act on the verb

`$ARGUMENTS` is the verb (or empty for status-only).

### No argument (default)

Print the table and stop. No side effects.

### `install`

- If row 1 is FAIL, print the .NET install link and stop. Per `BR-SECURITY-003`, this skill never installs language runtimes for the user.
- Otherwise run `Bash` `dotnet build src/HelpersSidecar/HelpersSidecar.csproj -c Debug`. Report the build outcome (success / failed with N errors).
- Re-print the table.

### `start`

This verb owns the **sidecar zombie sweep** per `BR-PROCESS-008`:

1. If row 1 is FAIL, print the install link and stop.
2. If row 3 is FAIL, instruct the user to run `/skill-bootstrap install` first and stop.
3. **Probe lifecycle state** by running `Bash`:
   - `dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll --lifecycle probe sidecar`
   - Parse the JSON. The `state` field is one of:
     - `RunningOurs` — sidecar already up under our PID file. Say "sidecar already running on :5050 (PID <X>)" and stop.
     - `NotRunning` — clean slate. Skip step 4, go to step 5.
     - `Zombie` — a previous run left a stale PID file or a non-bound process. Run step 4.
     - `Conflict` — port :5050 is held by something not ours. Print the JSON `reason` field, instruct the user to stop the holder or investigate, and stop. **Do not auto-kill** — `BR-SECURITY-003`.
4. **Sweep zombies** (only when `state == Zombie`):
   - `dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll --lifecycle sweep sidecar`
   - Reports `{ "swept": N }`. If `N >= 1`, the previous PID was killed and the PID file deleted; the next spawn is clean.
5. **Spawn** by calling `Bash` with `run_in_background: true`:
   - command: `dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll`
   - The sidecar writes its own PID file at `.claude/runtime/sidecar.pid` on startup and removes it on graceful shutdown.
6. **Poll healthz** up to 30 seconds (every 2 seconds): `curl http://127.0.0.1:5050/healthz -sS --max-time 2`. Declare ready when JSON arrives; declare timeout otherwise and tell the user how to read the background shell's output.
7. Re-print the table.

### `stop`

- Run `PowerShell` `Get-NetTCPConnection -LocalPort 5050 -State Listen -ErrorAction SilentlyContinue` and read the `OwningProcess` PIDs.
- For each PID, run `PowerShell` `Stop-Process -Id <pid>` (graceful; falls back to `-Force` only if the graceful stop times out — in practice the sidecar's `ApplicationStopping` hook clears the PID file on its way down).
- Re-probe `curl http://127.0.0.1:5050/healthz -sS --max-time 2` and confirm it now returns `SIDECAR_DOWN`.
- Re-print the table.

If the user hits Ctrl-C in another terminal or force-kills the sidecar process (causing the PID file to be left behind), the next `/skill-bootstrap start` detects it as `Zombie` and sweeps it automatically — the user never has to clean up by hand.

### `stage` — green/blue zero-downtime rebuild (BR-PROCESS-011)

Stage builds the sidecar to a separate output directory and spawns
a "green" instance on port :5051 alongside the running blue
(:5050). Blue keeps serving requests; OTEL stays continuous.

Run via `Bash`:

```
dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll --lifecycle stage sidecar
```

The CLI returns JSON `{ component, Outcome, GreenPid, Reason }`:

- `Staged` — green is healthy on :5051. Proceed to `promote` when ready.
- `BuildFailed` — `dotnet build` returned non-zero; carries `BuildStdout` + `BuildStderr`.
- `AlreadyStaged` — green is already running. Run `discard` first.
- `HealthCheckFailed` — green spawned but :5051/healthz didn't respond within 30s.

### `promote` — atomic blue↔green swap (BR-PROCESS-012)

Promote runs the atomic-with-rollback swap:

1. Verify green is healthy.
2. Snapshot blue (`bin/Debug/` → `bin/Debug.bak/`).
3. Kill blue.
4. Copy `bin/Staging/*` → `bin/Debug/*`.
5. Restart blue from updated `bin/Debug/`.
6. Verify blue's `:5050/healthz`.
7. On healthy → kill green; promote complete.
8. On unhealthy → restore from `bin/Debug.bak/`, restart blue, leave green alive.

Run via `Bash`:

```
dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll --lifecycle promote sidecar
```

Returns JSON `{ component, Outcome, BluePid, Reason }`:

- `Promoted` — blue running new code; green killed.
- `RolledBack` — verify failed; blue restored from snapshot; green still running for inspection.
- `NoGreenStaged` — nothing to promote; run `stage` first.
- `GreenNotHealthy` — green is alive but unhealthy at promote time.

### `discard`

Kill green; leave blue unchanged. Run via `Bash`:

```
dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll --lifecycle discard sidecar
```

Returns JSON `{ component, Outcome, Reason }` with `Discarded`,
`NoGreenStaged`, or `NotStageable`.

## Artefacts this skill manages

`/skill-bootstrap` is the writer of two registered durable artefacts (`BR-PROCESS-015`):

- **`PID_FILE v1`** at `.claude/runtime/sidecar.pid` — the running sidecar's PID; written on `start` (and at `Program.cs` lifetime hook), removed on graceful shutdown, swept by `start` if a previous run left it behind.
- **`PROMOTE_SNAPSHOT v1`** at `src/HelpersSidecar/bin/Debug.bak/` — the previous blue binary kept around for `BR-PROCESS-012`'s rollback path. Written by `promote`'s atomic swap; cleaned up by the next successful `promote`.

Both are `RuntimeState` lifecycle (transient; never gitignored-vs-tracked confusion — both are gitignored). Querying them: `/domain-info cross-domain artefacts` (or, in v1 of the registry, look in `ArtefactSpecs.All` for entries with `Owner: null`).

## OTEL-independence

`/skill-bootstrap` deliberately does NOT probe the Go collector (`:13133`, `:13134`), does NOT touch `persistent-enrichments.json`, and does NOT assume OTEL is on. The Go collector is the OTEL *tenant* on top of the deterministic-helpers *platform*; its lifecycle is owned by `/otel up` and `/otel down`. This skill works the same whether OTEL is fully configured, partially configured, or absent.

## See also

- [HELP.md](HELP.md) — verb reference.
- `BR-PROCESS-001` (in `docs/business-rules.md`) — bootstrap-class skills are the only hand-rolled exception.
- `BR-SECURITY-003` — pre-conditions checked; nothing installed without explicit consent.
