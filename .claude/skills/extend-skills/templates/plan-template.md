# <topic title — one short sentence>

> Plan-N file produced by `/otel-extend` Phase 1. Replace each
> bracketed placeholder with the real content. Delete this
> blockquote before committing.

## Motivation

<Why are we making this change? What problem does it solve, what
opportunity does it open? Two or three sentences.>

## Files affected

| Path | Change |
|---|---|
| `path/to/file.ext` | <one-line summary of the change> |
| `path/to/another.ext` | <one-line summary> |

## Behavioural change

**Before:** <what the system does today>

**After:** <what the system does after this change>

## Test approach

<How we'll verify the change works. Reference business rules
that this change satisfies (BR-... IDs) and which existing tests
already cover them. List any new tests we need to add.>

## Rollback steps

If the change has to be reverted after landing, the rollback is:

1. `git revert <feat-commit-sha>` (filled in after Phase 2
   commits)
2. `git revert <chore-commit-sha>` (filled in after Phase 3 if
   binaries were rebuilt)
3. `git revert <test-commit-sha>` (filled in after Phase 4)

The plan-commit itself can be reverted independently with
`git revert <plan-commit-sha>` if we want to remove the plan
file from history too.

## Out of scope

<What this change explicitly does NOT do. Helps reviewers
understand the boundaries.>
