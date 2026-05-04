---
name: demo
description: Run a target's guided onboarding tour AND its full-stack integration test. /demo <target> [<demo>] resolves the target, fetches a structured DEMO_PLAN v1 from the sidecar, then invokes each chained step via the Skill tool — producing real claude_code.skill_activated events the collector records. PASS/FAIL is derived exclusively from output/telemetry.jsonl (BR-DEMO-008); the agent never claims a step passed. /demo with no args defaults to the otel domain's default demo case (BR-DEMO-001).
argument-hint: [<target>] [<demo>]
disable-model-invocation: false
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/demo/dispatch *) Skill(otel *) Skill(enrich *) Skill(weather *) Skill(skill-bootstrap start *)
---

This skill is an **orchestrator**. It has no `!` exec line because the `!` line fires before the agent turn — anything it dispatched would be invisible to the Claude Code harness's skill-tracking. The whole point of /demo post-Plan-23 is for chained `Skill` invocations to produce real `claude_code.skill_activated` events the collector records. PASS/FAIL is then derived from `output/telemetry.jsonl` — never from the agent's self-report.

Follow these instructions exactly:

## 1. Fetch the plan

Run this command via the **`Bash` tool** (not `!`):

```
curl http://127.0.0.1:5050/skills/demo/dispatch -sS --max-time 60 \
  --data-urlencode "session_id=${CLAUDE_SESSION_ID}" \
  --data-urlencode "args=$ARGUMENTS"
```

If the response begins with `PRECONDITION_FAIL:`, render that line back to the user and stop.

If the response contains a `RECOVERY_AVAILABLE v1:` marker, follow the **RECOVERY_AVAILABLE v1 — offer-then-chain (BR-SKILL-014)** section below before continuing.

If the response contains `DEMO RESULT: SKIPPED` (port conflict or no demo case), render the body to the user as-is and stop.

Otherwise the response carries:

- A pre-flight section (`STEP 00.x` rows) — render to the user as-is.
- A `DEMO_PLAN v1: target="<t>" target_kind="<k>" demo="<d>" steps=<n> run_id="<id>"` header — capture `run_id` and the target name (you will use both in step 3).
- A `DEMO_DESCRIPTION:` line — render to the user.
- One or more `STEP_INVOKE: number=<n> skill="<name>" args="<argv>" label="<text>" expect="<marker>"` lines.
- Zero or more `STEP_OBSERVE: number=<n> target="<file>" label="<text>"` lines.

## 2. Execute each step in order

For each `STEP_INVOKE` line:

1. Invoke the named skill via the **`Skill` tool** with the given `args`. Today's chain targets are `otel`, `enrich`, `weather`, and `skill-bootstrap start` — these are the only ones in `allowed-tools`.
2. Render the step's number + label + a one-line acknowledgement of the chained skill's response. **Do not claim PASS/FAIL.** The truth lives in OTEL.

For each `STEP_OBSERVE` line:

1. Read the `target` file via the **`Read` tool**.
2. Render the step's number + label + a one-line summary of what's in the file (record count, byte size, ticket-id reference counts, etc.). Read-only OTEL inspection.

The order in the plan is the order to execute. Skip nothing.

## 3. Render the report (finalize)

After the LAST step has been invoked, render `DEMO_REPORT v1` by calling the same dispatch endpoint with `finalize=<run_id>`:

```
curl http://127.0.0.1:5050/skills/demo/dispatch -sS \
  --data-urlencode "session_id=${CLAUDE_SESSION_ID}" \
  --data-urlencode "args=<target> finalize=<run_id>"
```

The sidecar reads `output/telemetry.jsonl` from the run's `StartedAt` to now, correlates `claude_code.skill_activated` events to the plan's `STEP_INVOKE` markers by `skill.name`, derives PASS/FAIL per step from event presence, and writes the report. The response includes:

- `DEMO_FINALIZE v1: target="..." demo="..." run_id="..." started_at="..." ended_at="..." jsonl_records=<n>`
- One `STEP NN: PASS|FAIL — <label>` row per plan step, each with the JSONL evidence (or its absence) on the next line.
- `DEMO RESULT: <pass>/<total> PASS` — the integration-test scoreboard (`BR-EXTEND-012`).
- `Report saved to: <path>` — the durable `DEMO_REPORT v1` markdown.

Render the response to the user verbatim.

## RECOVERY_AVAILABLE v1 — offer-then-chain (BR-SKILL-014)

If the dispatch response contains a line beginning `RECOVERY_AVAILABLE v1:` of the form `skill="<name>" verb="<verb>" reason="<rationale>"`:

1. Surface the marker and `reason` to the user.
2. Ask: "invoke `/<skill> <verb>` to recover?".
3. **Wait for explicit confirmation** ("yes" / "y" / "go"). Per `BR-SECURITY-003`, never auto-invoke.
4. On confirmation, invoke the named skill via the `Skill` tool — today `/otel up` and `/skill-bootstrap start` are wired (see `allowed-tools`). Re-fetch the plan via step 1 after recovery completes.
5. On refusal, show the manual command and stop.

The marker is suppressed when the down-state cannot be auto-recovered (e.g. `:4318` held by a non-project process — `BR-SECURITY-003` forbids recommending we stop a process we don't own).

## Output schema

`DEMO_REPORT v1` is written under `output/demo-reports/` (`BR-DEMO-004`) with the per-step JSONL evidence inlined. Schema-versioned per `BR-PROCESS-013`. Every PASS/FAIL is provable from the report's evidence section — `BR-DEMO-008` makes that the contract.
