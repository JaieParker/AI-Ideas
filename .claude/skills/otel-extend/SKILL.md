---
name: otel-extend
description: Self-modification flow for the OTEL project. Drafts a plan, applies changes, rebuilds, tests, and commits each phase under git so any phase can be reverted independently. Invoke only when /otel emits an EXTEND_REQUESTED marker, or when the user types /otel-extend directly. The flow gates each phase with explicit user confirmation.
argument-hint: [topic | revert | status]
disable-model-invocation: false
user-invocable: false
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/otel-extend/dispatch *) Bash(git *) Bash(go *) Bash(dotnet *) Read Edit Write Glob Grep
---

!`curl http://127.0.0.1:5050/skills/otel-extend/dispatch -sS --max-time 5 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'skill_dir=${CLAUDE_SKILL_DIR}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

The dispatch above ran the deterministic gathering work for the
flow (git state, plan-file scan, suggested next plan name). It
did NOT make any changes. Now you (Claude) drive the multi-phase
flow described in [`playbook.md`](playbook.md), gating each
phase on explicit user confirmation:

1. **Phase 0 — Pre-flight.** Confirm git is clean (per
   `BR-EXTEND-001`). If not a repo, run the `git init` + baseline
   + double-confirm dance (`BR-EXTEND-003`).
2. **Phase 1 — Plan.** Draft the change as the next plan file
   using the dispatch's suggested name and the
   [plan template](templates/plan-template.md). Commit with the
   `plan:` prefix (`BR-EXTEND-002`). Show the user; ask
   *"implement now?"*.
3. **Phase 2 — Implement.** Make the source changes. Show diff.
   Ask *"commit?"*. Commit with `feat(otel):` (or the right
   verb).
4. **Phase 3 — Build.** Run the build. Surface failure. Ask
   *"commit rebuilt artefacts?"*. Commit with `chore:` if yes.
5. **Phase 4 — Test.** Run the test suite. Show pass/fail. Ask
   *"keep / revert"*. Commit with `test:`.

[`phases.md`](phases.md) has the per-phase detail. [`commit-prefixes.md`](commit-prefixes.md)
is the canonical commit-prefix list for this flow.

If args was `revert`, the dispatch already returned the recent
extend-flow commits. Show them to the user, ask how far back to
revert, then run `git revert <range>` (preferred — keeps
history) unless the user explicitly asks for `git reset --hard
<sha>`.

If args was `status`, the dispatch already returned the current
state; just acknowledge.

Echo every commit SHA back to the user as it lands.
