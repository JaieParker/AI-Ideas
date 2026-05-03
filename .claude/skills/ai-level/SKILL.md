---
name: ai-level
description: Score skills against Anthropic's 4 D AI-fluency rubric (Delegation, Description, Discernment, Diligence). The deterministic half (allowed-tools tightness, BR citations, schema markers, durability hints) runs in the .NET sidecar; the judgement half (does the description disambiguate, does the body enable verification) is your job after reading the rendered AI_LEVEL_REPORT v1. /ai-level (no arg) — usage + skill counts. /ai-level <skill-name> — score one skill. /ai-level local — score every skill under .claude/skills/. Global scope is intentionally not supported in v1 per BR-SECURITY-003. BR-SKILL-013.
argument-hint: [<skill-name> | local]
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/ai-level/dispatch *)
---

!`curl http://127.0.0.1:5050/skills/ai-level/dispatch -sS --max-time 10 --data-urlencode 'session_id='"$CLAUDE_SESSION_ID"'' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: sidecar at :5050 unreachable. Run /skill-bootstrap start first.\n'`

You are running `/ai-level` — score skills against the AI-fluency 4 D rubric per `BR-SKILL-013`. The sidecar above ran the deterministic checks (frontmatter parsing, `allowed-tools` tightness per `BR-SKILL-009`, BR citations, schema-version markers, durability + reversal hints) and wrote the `AI_LEVEL_REPORT v1` to disk. If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

The output you just received contains:

- The scope label (a skill name, `local`, or empty for usage).
- Total score across all assessed skills (out of `n × 8`).
- The three weakest skills.
- The path to the saved report file.

**Now do the judgement half.** The sidecar can score whether `disable-model-invocation` is explicit and whether `allowed-tools` is tight enough — it cannot judge whether a description is *unambiguous*, whether a body's example output enables verification, or whether the rollback path is *complete*. That's the part of the rubric only a reading-Claude can score, per `BR-SKILL-006` and `BR-SKILL-012`.

For each skill scored:

1. Read the saved report at the path the helper printed.
2. Open the skill's `SKILL.md` (`.claude/skills/<name>/SKILL.md`).
3. For each dimension where the deterministic score is `Strong (2/2)`, ask the judgement question:
   - **Delegation:** does the body explicitly carve deterministic vs judgement?
   - **Description:** is the `description` specific enough to disambiguate triggering vs not?
   - **Discernment:** does the body's example output let a user verify correctness without re-running?
   - **Diligence:** does the body explain how to audit a past run (find the report, replay, compare)?
4. If your judgement disagrees with the deterministic score, amend the report file's evidence row with a `(judgement override: …)` line. Be brief — one line per disagreement.
5. After per-skill review, summarise the top 3 cross-cutting weaknesses in 1–2 sentences each. Concrete fixes only ("add `argument-hint: <foo>` to `weather`'s frontmatter") — never platitudes.

The report is **schema-versioned** (`AI_LEVEL_REPORT v1`) per `BR-PROCESS-013`. Re-running `/ai-level <scope>` overwrites the rendered markdown but writes a new timestamped file, so historical scores are preserved.

## Scopes

| Form | Result |
|---|---|
| `/ai-level` | Usage + count of skills found in `.claude/skills/`. Read-only. |
| `/ai-level <skill-name>` | One skill in this project. Resolves against `.claude/skills/<name>/SKILL.md`. |
| `/ai-level local` | Every skill under `.claude/skills/*/SKILL.md`. |

Global scope (`~/.claude/skills/`) is **intentionally not supported** in v1 per `BR-SECURITY-003`. The sidecar binds `127.0.0.1` and never reads outside the project root. A future plan will add the global scope behind an explicit startup flag.

## Self-test

`/ai-level ai-level` scores this skill against its own rubric. The `BR-SKILL-013` discipline says every skill must be self-assessable; the rubric-applier itself is the most credible test of the design — if `/ai-level ai-level` doesn't score 8/8, the skill is failing the rule it enforces.

## See also

- `BR-SKILL-013` (in `docs/business-rules.md`) — the rule this skill enforces.
- `BR-PROCESS-013` — schema-versioned report contract; `AI_LEVEL_REPORT v1` is in the catalogue.
- `BR-SKILL-006` / `BR-SKILL-012` — the deterministic-vs-judgement split this skill implements.
