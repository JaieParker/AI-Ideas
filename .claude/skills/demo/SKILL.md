---
name: demo
description: Run a target's guided onboarding tour AND its full-stack integration test. /demo <target> [<demo>] resolves the target (a domain today; per-skill targets land later), fetches a structured DEMO_PLAN v1 from the sidecar, then invokes each chained step via the Skill tool — producing real claude_code.skill_activated events the collector records. Plan-23 retired the in-process loopback chain that bypassed the harness; every chained step now traverses the real Claude Code skill path. /demo with no args defaults to the otel domain's default demo case (BR-DEMO-001).
argument-hint: [<target>] [<demo>]
disable-model-invocation: false
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/demo/dispatch *) Bash(curl http://127.0.0.1:5050/skills/demo/observe *) Skill(otel *) Skill(enrich *) Skill(weather *) Skill(skill-bootstrap start *)
---

This skill is an **orchestrator**: it does NOT run a `!` shell-exec preprocessing line. The `!` line fires before the agent turn starts, so anything it spawned would be invisible to the Claude Code harness's skill-tracking — and the entire point of `/demo` post-Plan-23 is to chain skills via the **`Skill` tool** so the harness emits `claude_code.skill_activated` events for every chained step. That is the integration-test signal `BR-DEMO-001` and `BR-DEMO-002` (amended) require.

Follow these instructions exactly:

## 1. Fetch the plan

Run this command via the **`Bash` tool** (not as a `!` line):

```
curl http://127.0.0.1:5050/skills/demo/dispatch -sS --max-time 60 \
  --data-urlencode "session_id=${CLAUDE_SESSION_ID}" \
  --data-urlencode "args=$ARGUMENTS"
```

If the response begins with `PRECONDITION_FAIL:`, render that line back to the user and stop.

If the response contains a `RECOVERY_AVAILABLE v1:` marker, follow the **RECOVERY_AVAILABLE v1 — offer-then-chain (BR-SKILL-014)** section below before continuing.

If the response contains `DEMO RESULT: SKIPPED` (port conflict or no demo case), render the body to the user as-is and stop.

Otherwise the response carries:

- A pre-flight section (`STEP 00.x` rows) — render to the user as-is so they can see the platform-level checks.
- A `DEMO_PLAN v1: target="<t>" target_kind="<k>" demo="<d>" steps=<n> run_id="<id>"` header — capture the `run_id`; you will POST it back per step.
- A `DEMO_DESCRIPTION:` line — render to the user.
- One or more `STEP_INVOKE: number=<n> skill="<name>" args="<argv>" label="<text>" expect="<marker>"` lines.
- Zero or more `STEP_OBSERVE: number=<n> target="<file>" label="<text>"` lines.
- A teardown section.

## 2. Execute each step in order

For each `STEP_INVOKE` line:

1. Invoke the named skill via the **`Skill` tool** with the given `args`. Today the chain targets `otel`, `enrich`, `weather`, and `skill-bootstrap start` — these are the only ones in `allowed-tools`.
2. Capture the response.
3. Determine `pass`: `true` if the response is non-empty AND (no `expect` clause, OR the `expect` substring appears in the response). `false` otherwise.
4. POST the result to `/skills/demo/observe` via the **`Bash` tool**:

   ```
   curl http://127.0.0.1:5050/skills/demo/observe -sS \
     --data-urlencode "run_id=<run_id>" \
     --data-urlencode "step=<n>" \
     --data-urlencode "pass=<true|false>" \
     --data-urlencode "detail=<one-line summary of what happened>" \
     --data-urlencode "started_at=<ISO-8601>" \
     --data-urlencode "ended_at=<ISO-8601>"
   ```

For each `STEP_OBSERVE` line:

1. Read the `target` file via the **`Read` tool**, OR call the same `/skills/demo/observe` curl above with a small summary in `detail` (e.g. record count, byte size, ticket-id reference counts). Pure read-only; no skill chain.
2. POST the result the same way as `STEP_INVOKE`. `pass=true` if the file exists and was readable.

Render each step's PASS/FAIL as `STEP NN: PASS|FAIL — <label>` followed by an indented detail line, so the user sees a numbered list.

## 3. Finalise

After the LAST step, POST `finalize=true` to flush the `DEMO_REPORT v1` markdown:

```
curl http://127.0.0.1:5050/skills/demo/observe -sS \
  --data-urlencode "run_id=<run_id>" \
  --data-urlencode "finalize=true"
```

The response includes `report=<path>` — render that path to the user so they can find the report. The response's `DEMO RESULT: <pass>/<total> PASS` line is the integration-test scoreboard (`BR-EXTEND-012`).

## RECOVERY_AVAILABLE v1 — offer-then-chain (BR-SKILL-014)

If the dispatch response contains a line beginning `RECOVERY_AVAILABLE v1:` of the form `skill="<name>" verb="<verb>" reason="<rationale>"`:

1. Surface the marker and `reason` to the user.
2. Ask: "invoke `/<skill> <verb>` to recover?".
3. **Wait for explicit confirmation** ("yes" / "y" / "go"). Per `BR-SECURITY-003`, never auto-invoke.
4. On confirmation, invoke the named skill via the `Skill` tool — today only `/otel up` and `/skill-bootstrap start` are wired (the matching `Skill(otel *)` and `Skill(skill-bootstrap start *)` entries are in `allowed-tools`). Re-fetch the plan via step 1 after recovery.
5. On refusal, show the manual command and stop.

The marker is suppressed when the down-state cannot be auto-recovered (e.g. `:4318` held by a non-project process — `BR-SECURITY-003` forbids recommending we stop a process we don't own).

## Output schema

`DEMO_REPORT v1` is written under `output/demo-reports/` (`BR-DEMO-004`) with each step's per-window OTEL records inlined. The console shows the live skill-chain; the report carries the full record. Schema-versioned per `BR-PROCESS-013`.
