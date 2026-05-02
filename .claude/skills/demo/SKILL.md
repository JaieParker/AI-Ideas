---
name: demo
description: Run the full 15-step OTEL enrichment demo end-to-end against the local collector. Sets persistent attributes (user/workstation/version), sets a per-session ticket reference (JA-0001), runs /weather working and failing, shows the OTEL logs, then changes the ticket to JA-0002 and re-runs. Demonstrates the project's value in one invocation.
argument-hint: (no arguments)
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/demo/dispatch *)
---

!`curl http://127.0.0.1:5050/skills/demo/dispatch -sS --max-time 10 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

Render the step-by-step output above as a numbered list for the user. If any step reported the collector or sidecar is unreachable, surface that prominently with the suggested fix (`/otel`).
