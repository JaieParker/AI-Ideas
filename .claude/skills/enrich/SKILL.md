---
name: enrich
description: Manage per-session OTEL enrichment attributes. /enrich <key> <value> sets one; --remove drops one; --clear wipes all; --show lists current. User-only — only the human types /enrich. The new value is stamped on every span/log/metric Claude Code emits from the next OTLP flush onward.
argument-hint: <key> <value> | --remove <key> | --clear | --show
disable-model-invocation: true
allowed-tools: Bash(node *)
---

!`node '${CLAUDE_SKILL_DIR}/scripts/enrich.mjs' '${CLAUDE_SESSION_ID}' '$ARGUMENTS'`

In one short sentence, acknowledge the enrichment change shown above (or relay the error if the helper failed).
