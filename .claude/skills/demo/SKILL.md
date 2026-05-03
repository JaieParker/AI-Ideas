---
name: demo
description: Run a domain's guided onboarding tour. /demo <domain> runs that domain's IDomainDemo (BR-EXTEND-010) — for OTEL, the 14-step skill chain that brings the collector up, configures persistent + per-session enrichments, runs /weather working + failing, observes JSONL records, changes the ticket id, re-runs, and tears down. /demo with no argument defaults to the OTEL domain. The same dispatch is the project's full-stack integration test surface (every step emits a stable PASS|FAIL marker).
argument-hint: [<domain>] (defaults to 'otel')
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/demo/dispatch *)
---

!`curl http://127.0.0.1:5050/skills/demo/dispatch -sS --max-time 60 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

Render the step-by-step output above as a numbered list for the user. If any step reported the collector or sidecar is unreachable, surface that prominently with the suggested fix (`/otel`).

**Output:** every `/demo` run writes a durable `DEMO_REPORT v1` markdown file (`BR-DEMO-004`) at `output/demo-reports/<UTC-ts>-<domain>.md`. The console shows the 14-step chain; the report carries the same steps plus the OTEL records each step produced, schema-versioned per `BR-PROCESS-013` for future audit.
