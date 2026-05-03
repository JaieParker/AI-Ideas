---
name: demo
description: Run a domain's guided onboarding tour. /demo <domain> runs that domain's IDomainDemo (BR-EXTEND-010) — for OTEL, the 14-step skill chain that brings the collector up, configures persistent + per-session enrichments, runs /weather working + failing, observes JSONL records, changes the ticket id, re-runs, and tears down. /demo with no argument defaults to the OTEL domain. The same dispatch is the project's full-stack integration test surface (every step emits a stable PASS|FAIL marker). Per BR-SKILL-014, when the pre-flight detects a recoverable down-state with a known fix skill, the dispatch emits a RECOVERY_AVAILABLE v1 marker and this body offers to chain the recovery on user confirmation.
argument-hint: [<domain>] (defaults to 'otel')
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/demo/dispatch *) Bash(dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll --lifecycle probe sidecar*) Skill(otel up *) Skill(skill-bootstrap start *)
---

!`curl http://127.0.0.1:5050/skills/demo/dispatch -sS --max-time 60 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS' || dotnet src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll --lifecycle probe sidecar 2>/dev/null || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050 AND lifecycle CLI unavailable. Run /skill-bootstrap status, then /skill-bootstrap install, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

If the helper output is a JSON object beginning with `{"State":"NotRunning"` or `{"State":"Zombie"`, the dispatch curl failed (sidecar down) and the lifecycle CLI fell through. Emit the recovery offer to the user verbatim:

```
RECOVERY_AVAILABLE v1: skill="skill-bootstrap" verb="start" reason="deterministic-helpers sidecar :5050 down — required for /demo dispatch"
```

Then follow the **RECOVERY_AVAILABLE v1 — offer-then-chain** section below. On user confirmation, invoke `/skill-bootstrap start` via the `Skill` tool; on success, re-invoke `/demo`.

If the helper output is a JSON object beginning with `{"State":"Conflict"`, port `:5050` is held by a non-project process. **Suppress the recovery marker** per `BR-SECURITY-003` — we never recommend stopping a process we don't own. Show the JSON's `Reason` field to the user and stop.

Otherwise (the dispatch curl returned its normal output), render the step-by-step output as a numbered list for the user. If any step reported the collector or sidecar is unreachable, surface that prominently with the suggested fix (`/otel`).

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
