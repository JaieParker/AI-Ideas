---
name: otel
description: Bootstrap, runtime control, and persistent config for the OTEL collector and helpers sidecar. /otel alone reports status; on/off/status/restart toggle this session's collection; set/get/unset/config manage persistent enrichments; extend chains to /otel-extend; help prints the command list. User-only.
argument-hint: [on|off|status|restart|help|set <k>:<v>|get <k1> <k2>...|unset <k>|config [clear]|extend [topic]]
disable-model-invocation: true
allowed-tools: Bash(curl *) Skill
---

!`curl -sS http://127.0.0.1:5050/skills/otel/dispatch --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'skill_dir=${CLAUDE_SKILL_DIR}' --data-urlencode 'args=$ARGUMENTS'`

If the helper above emitted a line beginning `EXTEND_REQUESTED:`, invoke the `otel-extend` skill via the `Skill` tool, passing the topic (everything after `topic="` up to the closing quote) as the argument. Otherwise acknowledge the helper's output in one short line.
