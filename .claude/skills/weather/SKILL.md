---
name: weather
description: Show the current weather. With no argument, uses IP-based location. With an argument like "London" or "94103", reports for that location. Use when the user asks "what's the weather?" or invokes /weather directly.
argument-hint: [location]
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/weather/dispatch *)
---

!`curl http://127.0.0.1:5050/skills/weather/dispatch -sS --max-time 5 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

Briefly summarise the weather shown above in one short sentence.
