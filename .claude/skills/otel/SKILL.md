---
name: otel
description: Bootstrap, runtime control, and persistent config for the OTEL collector and helpers sidecar. /otel alone reports status; on/off/status/restart toggle this session's collection; set/get/unset/config manage persistent enrichments; extend chains to /otel-extend; help prints the command list. User-only.
argument-hint: [on|off|status|restart|help|set <k>:<v>|get <k1> <k2>...|unset <k>|config [clear]|extend [topic]]
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/otel/dispatch *) Skill(extend-skills *)
---

!`curl http://127.0.0.1:5050/skills/otel/dispatch -sS --max-time 5 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'skill_dir=${CLAUDE_SKILL_DIR}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

If the helper above emitted a line beginning `EXTEND_REQUESTED:`, invoke the `extend-skills` skill via the `Skill` tool, passing the domain and topic together as `otel <topic>` (the marker carries `domain="otel"` and `topic="..."`; concatenate them as `otel <topic>` for the chained skill's first argument). Otherwise acknowledge the helper's output in one short line.
