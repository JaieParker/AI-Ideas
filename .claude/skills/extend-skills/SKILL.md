---
name: extend-skills
description: Domain-aware self-modification flow for this project. Drafts a plan, applies changes, rebuilds, tests, and commits each phase under git so any phase can be reverted independently. Invoke as /extend-skills <domain> [<topic>] (or /extend-skills <domain> revert | status). Domain is required and resolved via IDomainResolver — currently 'otel' is the only registered domain; future domains (e.g. kai-platform) will plug in via IDomain. The flow gates each phase with explicit user confirmation.
argument-hint: <domain> [<topic> | revert | status]
disable-model-invocation: false
user-invocable: false
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/extend-skills/dispatch *) Bash(git *) Bash(go *) Bash(dotnet *) Read Edit Write Glob Grep
---

!`curl http://127.0.0.1:5050/skills/extend-skills/dispatch -sS --max-time 5 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'skill_dir=${CLAUDE_SKILL_DIR}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

The dispatch above ran the deterministic gathering work for the
flow against the resolved domain (git state, plan-file scan
parameterised by the domain's `PlanFileConventions`, suggested
next plan name). It did NOT make any changes. Now you (Claude)
drive the multi-phase flow described in [the domain's playbook](../../../docs/otel/playbook.md) (Plan-9: per-domain authoritative location),
gating each phase on explicit user confirmation:

1. **Phase 0 — Pre-flight.** Confirm git is clean (per
   `BR-EXTEND-001`). If not a repo, run the `git init` + baseline
   + double-confirm dance (`BR-EXTEND-003`). When the dispatch
   output emits a `PLAN_TAG_ENRICHMENT` line, **run the
   `/enrich plan <filename>` command verbatim before proceeding**
   (`BR-EXTEND-009` — every OTEL record from this flow is tagged).
2. **Phase 1 — Plan.** Draft the change as the next plan file
   using the dispatch's suggested name and the
   [plan template](templates/plan-template.md). Commit with the
   domain's plan prefix (`BR-EXTEND-002` — read from the dispatch
   output's "Phase 1" line). Show the user; ask *"implement now?"*.
3. **Phase 1.5 — Architecture review.** Invoke
   `/architecture-review <plan-file>` (`BR-PROCESS-009`). The
   architect emits an `ARCHITECTURE_REVIEW v1` response per the
   schema embedded in the dispatch's prompt. Read it. For each
   `ARCHITECTURE_DECISION_REQUIRED` block, ask the user to pick
   one of: **Evolve** (amend BR text), **Constrain** (rework the
   plan), **Defer** (capture as open question), **Override**
   (deliberate one-off with one-line justification). Record the
   resolution in the plan file's "Architecture review decisions"
   section. Phase 2 does NOT proceed until every `EXTENDS` row
   has a recorded decision.
4. **Phase 2 — Implement.** Make the source changes inside the
   domain's `GovernedGlobs`. Show diff. Ask *"commit?"*. Commit
   with the domain's implement prefix (e.g. `feat(otel):` for the
   OTEL domain).
5. **Phase 3 — Build.** Run the build. Surface failure. Ask
   *"commit rebuilt artefacts?"*. Commit with `chore:` if yes.
6. **Phase 4 — Test.** Run the test suite. Show pass/fail. Ask
   *"keep / revert"*. Commit with `test:`.

[`phases.md`](phases.md) has the per-phase detail. [`commit-prefixes.md`](commit-prefixes.md)
is the canonical commit-prefix list — domains may extend this set
via their `IDomain.Commits` configuration.

If args was `<domain> revert`, the dispatch already returned the
recent extend-flow commits filtered by the domain's commit
prefixes. Show them to the user, ask how far back to revert, then
run `git revert <range>` (preferred — keeps history) unless the
user explicitly asks for `git reset --hard <sha>`.

If args was `<domain> status`, the dispatch already returned the
current state; just acknowledge.

If the dispatch returned an "unknown domain" error, surface the
error verbatim — the user typed an invalid domain name; they need
to choose one of the listed `KnownNames`.

Echo every commit SHA back to the user as it lands.
