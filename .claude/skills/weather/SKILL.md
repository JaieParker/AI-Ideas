---
name: weather
description: Show the current weather. With no argument, uses IP-based location. With an argument like "London" or "94103", reports for that location. Use when the user asks "what's the weather?" or invokes /weather directly.
argument-hint: [location]
allowed-tools: Bash(node *)
---

!`node '${CLAUDE_SKILL_DIR}/scripts/weather.mjs' '$ARGUMENTS'`

Briefly summarise the weather shown above in one short sentence.
