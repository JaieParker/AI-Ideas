---
name: domain-info
description: Read-only query over a domain's knowledge slices. /domain-info <domain> returns every slice (name, plan-files, commits, governed-globs, playbook-path, glossary, business-rules-path, trusted-references). /domain-info <domain> <slices> returns just the comma-separated subset (e.g. /domain-info otel glossary,trusted-references). Useful for new contributors orienting to a domain, for the future architecture-review agent (Plan-6) when citing TrustedReferences, and for any future cross-domain audit. BR-EXTEND-006 + BR-EXTEND-008.
argument-hint: <domain> [<slices>]
disable-model-invocation: false
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/domain-info/dispatch *)
---

!`curl http://127.0.0.1:5050/skills/domain-info/dispatch -sS --max-time 5 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

The dispatch returned a JSON document with the requested slices
(`DOMAIN_INFO v1` shape — inline-only, not registered as a
durable artefact since nothing is written to disk; named in
`BR-PROCESS-013`'s catalogue for visibility).

Render it for the user — keep the JSON intact (don't paraphrase
slice contents); a brief one-line summary above it is fine.

Available slice names (use comma-separated, or omit for `all`):

- `name` — the domain's stable identifier
- `plan-files` — `PlanFileConventions` (prefix, number floor, suffix)
- `commits` — per-phase commit-message prefixes (BR-EXTEND-002)
- `governed-globs` — paths the self-modification flow governs (BR-PROCESS-001)
- `playbook-path` — domain's flow playbook
- `glossary` — domain's ubiquitous-language terms
- `business-rules-path` — domain's BR document
- `trusted-references` — curated authoritative external sources (BR-EXTEND-008)
- `artefacts` — projection from `IArtefactRegistry` of every artefact owned by this domain (BR-PROCESS-015)
- `all` — every slice (default)

Examples:

```
/domain-info otel
/domain-info otel glossary
/domain-info otel trusted-references,plan-files
```

If the dispatch returned an "unknown domain" error, surface the
error verbatim — the user typed an invalid domain name.
