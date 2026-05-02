---
name: enrich
description: Manage per-session OTEL enrichment attributes. /enrich <key> <value> sets one; --remove drops one; --clear wipes all; --show lists current. User-only — only the human types /enrich. The new value is stamped on every span/log/metric Claude Code emits from the next OTLP flush onward.
argument-hint: <key> <value> | --remove <key> | --clear | --show
disable-model-invocation: true
allowed-tools: Bash(curl *)
---

!`curl -sS http://127.0.0.1:5050/skills/enrich/dispatch --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS'`

In one short sentence, acknowledge the enrichment change shown above (or relay the error if the helper failed).
