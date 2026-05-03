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

**Validation rules:** keys must match `^[a-z][a-z0-9_.\-]*$` and be ≤ 64 chars (`BR-ENRICH-001`); values must be ≤ 4096 chars (`BR-ENRICH-002`). Per-session enrichments are isolated by `session.id` and stamped on every OTLP record (`OTLP v1`) the collector emits from the next flush onward, alongside any persistent enrichments managed via `/otel set`.
