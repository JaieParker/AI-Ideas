---
name: demo
description: Run a domain's guided onboarding tour. /demo <domain> runs that domain's IDomainDemo (BR-EXTEND-010) — for OTEL, the 14-step skill chain that brings the collector up, configures persistent + per-session enrichments, runs /weather working + failing, observes JSONL records, changes the ticket id, re-runs, and tears down. /demo with no argument defaults to the OTEL domain. The same dispatch is the project's full-stack integration test surface (every step emits a stable PASS|FAIL marker). Per BR-SKILL-014, when the pre-flight detects a recoverable down-state with a known fix skill, the dispatch emits a RECOVERY_AVAILABLE v1 marker and this body offers to chain the recovery on user confirmation.
argument-hint: [<domain>] (defaults to 'otel')
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/demo/dispatch *) Skill(otel up *)
---

!`curl http://127.0.0.1:5050/skills/demo/dispatch -sS --max-time 60 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

Render the step-by-step output above as a numbered list for the user. If any step reported the collector or sidecar is unreachable, surface that prominently with the suggested fix (`/otel`).

## RECOVERY_AVAILABLE v1 — offer-then-chain (BR-SKILL-014)

Scan the dispatch output for a line beginning `RECOVERY_AVAILABLE v1:`. The marker shape is:

```
RECOVERY_AVAILABLE v1: skill="<name>" verb="<verb>" reason="<short rationale>"
```

If present:

1. Surface the marker and the `reason` to the user.
2. Ask: "invoke `/<skill> <verb>` to bring the collector up?".
3. **Wait for explicit confirmation** ("yes" / "y" / "go"). Per `BR-SECURITY-003`, never auto-invoke — the marker is an offer, not a chain.
4. On confirmation, invoke the named skill via the `Skill` tool — today only `/otel up` is wired (`allowed-tools` carries the matching `Skill(otel up *)` entry). After it returns, re-invoke `/demo` to continue the live steps.
5. On refusal, show the manual command and stop.

The marker is suppressed when the down-state is not auto-recoverable (e.g. `:4318` held by a non-project process — `BR-SECURITY-003` forbids recommending we stop a process we don't own).

**Output:** every `/demo` run writes a durable `DEMO_REPORT v1` markdown file (`BR-DEMO-004`) at `output/demo-reports/<UTC-ts>-<domain>.md`. The console shows the 14-step chain; the report carries the same steps plus the OTEL records each step produced, schema-versioned per `BR-PROCESS-013` for future audit.
