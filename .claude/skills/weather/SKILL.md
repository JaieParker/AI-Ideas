---
name: weather
description: Show the current weather. With no argument, uses IP-based location. With an argument like "London" or "94103", reports for that location. Use when the user asks "what's the weather?" or invokes /weather directly.
argument-hint: [location]
allowed-tools: Bash(curl *)
---

!`curl -sS http://127.0.0.1:5050/skills/weather/dispatch --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS'`

Briefly summarise the weather shown above in one short sentence.
