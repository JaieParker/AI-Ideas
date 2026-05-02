# Commit-prefix conventions for the /otel-extend flow

Per `BR-EXTEND-002`, each phase commits separately with a
documented prefix so any phase can be reverted without touching
the others.

| Phase | Prefix | Example |
|---|---|---|
| 1 — Plan | `plan: <topic> in <plan-file>` | `plan: rate limiting in The-OTEL-Plan-3-rate-limiting.md` |
| 2 — Implement | `feat(otel): <topic>` | `feat(otel): rate limiting on the helpers sidecar` |
|   | `fix(otel): <topic>` | `fix(otel): off-by-one in plan-numbering` |
|   | `refactor(otel): <topic>` | `refactor(otel): extract dispatch into use-case classes` |
|   | `docs(otel): <topic>` | `docs(otel): clarify enrichment ordering` |
| 3 — Build | `chore: rebuild collector for <topic>` | `chore: rebuild collector for rate-limiting` |
|   | `chore: rebuild helpers for <topic>` | `chore: rebuild helpers for plan-scanner-fix` |
| 4 — Test | `test: green for <topic>` | `test: green for rate-limiting` |
|   | `test: <topic> failing X cases` | `test: rate-limiting failing 2 cases (deferred)` |

## Why these prefixes specifically

Each prefix is a **conventional-commit verb** with a
project-specific scope. `plan:` is non-standard but unique to
this flow; the others map cleanly onto the
[Conventional Commits](https://www.conventionalcommits.org/)
vocabulary. A `git revert` for phase isolation works on any of
these because each phase is exactly one commit.

## What about non-flow commits?

Commits NOT made via `/otel-extend` use the standard
conventional-commit prefixes (`feat:`, `fix:`, `chore:`,
`docs:`, `test:`, `refactor:`) without the `(otel)` scope. The
scope marker is what tells `/otel-extend revert` which commits
are part of an extend flow.
