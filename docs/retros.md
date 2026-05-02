# Retrospectives

Brief retros after every user-requested change of meaningful
scope, per `BR-PROCESS-002`. Newest entry at the top so the most
recent learning is the first thing a reader sees.

Format: three sections — *What happened*, *What could be
improved*, *Strategies for next time*. Bullets only. No
platitudes. ~200 words total.

---

## 2026-05-02 — Add BR-PROCESS-002 (retro-after-every-change)

**What happened**

- User asked for a retro after every requested change.
- Added `BR-PROCESS-002` to the rule register, a CLAUDE.md
  section spelling out the format and length cap, and this
  retros log file with its first entry (this one — meta but
  honest).
- All in one commit; no source code touched.

**What could be improved**

- The retro rule is going into a session where five other
  process-level rules (`BR-SKILL-007/008/009`,
  `BR-PROCESS-001`, `BR-CODE-001`) have already been added in
  the same conversation. Each landed in its own commit, but they
  weren't surfaced as "we are now hardening process discipline"
  — they read as a string of small additions. A summary commit
  ("project process discipline pass") at the end would have
  made the intent visible.
- The retro itself only ever triggers if I remember the rule
  exists. CLAUDE.md is loaded into every session, so this is
  reasonably reliable — but the same critique that applies to
  `BR-PROCESS-001` (soft enforcement only) applies here.

**Strategies for next time**

- When two or more process rules land in the same session, end
  the session with a "process pass: rules added X/Y/Z" summary
  pointer in `docs/process-incidents.md` or this file.
- Pair the retro rule with the CI-level lint that already
  parses test names for BR IDs; extend it to flag commits whose
  message contains `feat:` or `fix:` but the response that
  produced them had no retro section. Soft signal, no hook.
- Keep retros short. The discipline is "make it concrete and
  useful", not "fill three paragraphs". If a section is empty,
  write one sentence that says so honestly.
