---
name: skill-bootstrap
description: Bootstrap and lifecycle for the .NET deterministic-helpers sidecar — the platform every other skill in this project depends on. Probes pre-requirements (.NET 10 SDK, sidecar source present, sidecar built, port 5050 free or owned by sidecar, healthz reachable) and prints a structured PASS/FAIL table. Verbs - no arg (status only, read-only), install (dotnet build), start (spawn sidecar in background and poll healthz), stop (terminate the listener on :5050). OTEL-independent - this is about the platform, not the Go collector. The collector's lifecycle is owned by /otel up / /otel down.
argument-hint: [install | start | stop] (no arg = status table only)
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/healthz *) Bash(dotnet --version) Bash(dotnet --list-sdks) Bash(dotnet build src/HelpersSidecar/HelpersSidecar.csproj *) Bash(dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll) PowerShell(Get-NetTCPConnection *) PowerShell(Stop-Process *) Read Write Glob
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

- If row 1 is FAIL, print the install link and stop.
- If row 3 is FAIL, instruct the user to run `/skill-bootstrap install` first and stop.
- If row 5 is already PASS, say "sidecar already running on :5050 (uptime <N>s, version <X>)" and stop.
- Otherwise spawn the sidecar by calling `Bash` with `run_in_background: true`:
  - command: `dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll`
  - this command does NOT match the tightly-prefixed `Bash(dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll)` permission in any wider sense — it is *exactly* that command.
- After spawn, poll the curl probe up to 30 seconds (every 2 seconds): re-run `curl http://127.0.0.1:5050/healthz -sS --max-time 2`. Declare ready when JSON arrives; declare timeout otherwise and tell the user how to read the background shell's output.
- Re-print the table.

### `stop`

- Run `PowerShell` `Get-NetTCPConnection -LocalPort 5050 -State Listen -ErrorAction SilentlyContinue` and read the `OwningProcess` PIDs.
- For each PID, run `PowerShell` `Stop-Process -Id <pid> -Force`.
- Re-probe `curl http://127.0.0.1:5050/healthz -sS --max-time 2` and confirm it now returns `SIDECAR_DOWN`.
- Re-print the table.

## OTEL-independence

`/skill-bootstrap` deliberately does NOT probe the Go collector (`:13133`, `:13134`), does NOT touch `persistent-enrichments.json`, and does NOT assume OTEL is on. The Go collector is the OTEL *tenant* on top of the deterministic-helpers *platform*; its lifecycle is owned by `/otel up` and `/otel down`. This skill works the same whether OTEL is fully configured, partially configured, or absent.

## See also

- [HELP.md](HELP.md) — verb reference.
- `BR-PROCESS-001` (in `docs/business-rules.md`) — bootstrap-class skills are the only hand-rolled exception.
- `BR-SECURITY-003` — pre-conditions checked; nothing installed without explicit consent.
