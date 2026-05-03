# Phase reference card

A one-page summary of the gates and commit shapes per phase.
Use this when [`playbook.md`](playbook.md) is too long to scan.

| # | Phase | Gate question | Commit prefix on accept |
|---|---|---|---|
| 0 | Pre-flight | git clean? if no repo, init + baseline? | (none — pre-condition) |
| 1 | Plan | "implement now? [y/n/edit]" | `plan: <topic> in <plan-file>` |
| 2 | Implement | "commit these changes? [y/n/show/abort]" | `feat(otel): <topic>` (or fix/refactor/docs) |
| 3 | Build | "commit rebuilt artefacts? [y/n]" | `chore: rebuild collector for <topic>` (or helpers) |
| 4 | Test | "keep / revert?" | `test: green for <topic>` (or describes failure) |

## State persistence between phases

The flow does not maintain its own state file. State lives in
git: each phase's commit is the resumable checkpoint. If you
have to abandon a flow mid-way and resume later, look at the
last commit's prefix to know where you are.

## Failure handling per phase

- **Phase 1 plan rejected:** plan file commits anyway (so the
  draft survives); no Phase 2 commit happens.
- **Phase 2 abort:** `git checkout -- .` reverts working tree
  to the Phase 1 commit. Plan file stays.
- **Phase 3 build failure:** loop back to Phase 2 with the
  fix, OR revert per Phase 4 revert behaviour.
- **Phase 4 test failure:** explicit user choice — revert,
  diagnose, or keep.
