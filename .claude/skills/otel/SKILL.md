---
name: otel
description: Bootstrap, runtime control, and persistent config for the OTEL collector and helpers sidecar. /otel alone reports status; on/off/status/restart toggle this session's collection; set/get/unset/config manage persistent enrichments; up/down own the collector tier's lifecycle (BR-OTEL-006); extend chains to /extend-skills; help prints the command list. Per BR-SKILL-014, when the dispatch detects the sidecar can recover from skill-bootstrap, this body offers the chain.
argument-hint: [on|off|up|down|status|restart|help|set <k>:<v>|get <k1> <k2>...|unset <k>|config [clear]|extend [topic]]
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/otel/dispatch *) Skill(extend-skills *) Skill(skill-bootstrap start *)
---

!`curl http://127.0.0.1:5050/skills/otel/dispatch -sS --max-time 5 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'skill_dir=${CLAUDE_SKILL_DIR}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

## RECOVERY_AVAILABLE v1 — offer-then-chain (BR-SKILL-014)

Scan the dispatch output for a line beginning `RECOVERY_AVAILABLE v1:`. The marker shape is `skill="<name>" verb="<verb>" reason="<rationale>"`. If present:

1. Surface the marker and `reason` to the user.
2. Ask: "invoke `/<skill> <verb>` to recover?".
3. Wait for explicit confirmation. Per `BR-SECURITY-003`, never auto-invoke.
4. On confirmation, invoke the named skill via the `Skill` tool — `/skill-bootstrap start` is the wired-up case (`allowed-tools` carries `Skill(skill-bootstrap start *)`). Re-invoke `/otel` after.

If the helper above emitted a line beginning `EXTEND_REQUESTED:`, invoke the `extend-skills` skill via the `Skill` tool, passing the domain and topic together as `otel <topic>` (the marker carries `domain="otel"` and `topic="..."`; concatenate them as `otel <topic>` for the chained skill's first argument). Otherwise acknowledge the helper's output in one short line.

**Output schemas this skill brokers:** `/otel up` precedes the collector's `OTLP v1` records appearing in `output/telemetry.jsonl`; `/demo` (downstream) writes `DEMO_REPORT v1` (`BR-DEMO-004`); persistent enrichments managed via `/otel set` apply to every emitted record (`BR-OTEL-001`, `BR-ENRICH-001`).
