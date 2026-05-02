---
name: enrich
description: Manage per-session OTEL enrichment attributes. /enrich <key> <value> sets one; --remove drops one; --clear wipes all; --show lists current. User-only — only the human types /enrich. The new value is stamped on every span/log/metric Claude Code emits from the next OTLP flush onward.
argument-hint: <key> <value> | --remove <key> | --clear | --show
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/enrich/dispatch *)
---

!`curl http://127.0.0.1:5050/skills/enrich/dispatch -sS --max-time 5 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

In one short sentence, acknowledge the enrichment change shown above (or relay the error if the helper failed).
