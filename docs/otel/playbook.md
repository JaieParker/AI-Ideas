# /otel-extend playbook

The multi-phase flow Claude drives when `/otel-extend` is
invoked. Each phase is **gated** on explicit user confirmation
and committed separately so any phase can be reverted without
disturbing the others.

## Phase 0 — Pre-flight (`BR-EXTEND-001`, `BR-EXTEND-003`)

Goal: ensure git is clean and a repo exists.

1. The dispatch endpoint already ran `git status --short` and
   reported state. Inspect it.
2. If **not a git repo**: tell the user. Ask *"initialise a
   local git repo and commit current state as baseline? Without
   git, changes cannot be safely reverted."* — on yes, run
   `git init && git add . && git commit -m "baseline:
   pre-extension snapshot"`. On no, warn explicitly:
   *"proceeding without git means any change is permanent. You
   will have no automatic revert path. Continue anyway?"* —
   require a second `yes` before continuing. Flag in every
   subsequent confirmation that revert is unavailable.
3. If git repo with **uncommitted changes**: refuse to proceed.
   Offer to `git stash push -u -m "pre-extend"` for the user.
4. If clean: proceed to Phase 1.

## Phase 1 — Plan (`BR-EXTEND-002`, `BR-EXTEND-004`, `BR-EXTEND-005`)

Goal: draft a plan file that names the change.

1. Use the dispatch endpoint's suggested plan filename — it
   already applied `BR-EXTEND-004` numbering and `BR-EXTEND-005`
   slug normalisation.
2. If the user did not supply a topic, ask for one (free text)
   and call the dispatch again with it (or call
   `/helpers/topics/slugify` directly).
3. Open [`templates/plan-template.md`](templates/plan-template.md).
   Fill in: motivation, files affected, behavioural change
   (before/after), test approach, rollback steps.
4. Save as `<project-root>/<suggested-plan-name>` (e.g.
   `The-OTEL-Plan-2-fix-the-foo.md`).
5. Stage and commit with the `plan:` prefix:
   ```
   git add <plan-file>
   git commit -m "plan: <topic> in <plan-file>"
   ```
6. Echo the commit SHA to the user.
7. Show the plan content; ask *"implement now? [yes / no /
   edit]"*.
   - `edit` → loop back to step 3 with the user's edits.
   - `no` → exit cleanly. The plan file remains for later
     resumption.
   - `yes` → proceed to Phase 2.

## Phase 2 — Implement

Goal: apply the source changes called out in the plan.

1. Read the plan's "Files affected" list.
2. For each file: use `Edit` / `Write` to make the change.
3. After all changes:
   - Run `git diff --stat` to summarise.
   - Offer `git diff` if the user wants the full diff.
   - Ask *"commit these changes? [yes / no / show-full-diff /
     abort]"*.
4. On `yes`: `git add -A && git commit -m "feat(otel):
   <topic>"` (or `fix:`, `refactor:`, `docs:` if more apt).
   Echo SHA.
5. On `abort`: `git checkout -- .` to revert working tree to
   the plan commit. Inform the user the plan file remains.

## Phase 3 — Build

Goal: rebuild any binaries the change affects.

1. Detect what to build:
   - If `src/HelpersSidecar/**` was touched → `dotnet build`.
   - If `components/**` was touched → `ocb --config=manifest.yaml`.
   - Both → both.
2. Run the build. Surface any failure verbatim.
3. On success: stage updated artefacts (in `dist/`); ask
   *"commit rebuilt binaries? [yes / no]"*. On yes,
   `git commit -m "chore: rebuild collector for <topic>"` (or
   `chore: rebuild helpers for <topic>`).
4. On failure: ask *"fix and rebuild / revert / abort?"* and
   loop accordingly.

## Phase 4 — Test

Goal: prove the change is green.

1. Detect tests to run:
   - `dotnet test` for sidecar changes.
   - `go test ./components/...` for collector changes.
   - Custom integration suite if any.
2. Run tests. Show pass/fail.
3. On all-pass:
   - `git commit --allow-empty -m "test: green for <topic>"`
   - Ask *"keep / revert?"*. On `keep` end the flow; echo all
     commit SHAs collected during the flow.
4. On any failure: ask *"revert / diagnose / keep with failing
   tests?"*. Behave per choice.

## Revert (callable at any phase, or after the flow)

The user types `/otel-extend revert`. The dispatch endpoint
returns recent extend-flow commits (parsed from `git log` for
messages prefixed with `plan:`, `feat(otel):`, `chore:`,
`test:`). Show them. Ask how far back to revert.

Default: `git revert <range>` — keeps history, easy to undo.
Only use `git reset --hard <sha>` if the user explicitly asks
for it; warn that the rewrite drops the intervening commits.

## What the flow does NOT do

- Never `git push --force` (or push at all without explicit
  user direction).
- Never modify `.gitignore` outside what the plan calls out.
- Never delete files without staging the deletion as part of
  an approved commit.
- Never touch the user's git config or signing settings.
- Never operate on a dirty working tree (refused in Phase 0).
