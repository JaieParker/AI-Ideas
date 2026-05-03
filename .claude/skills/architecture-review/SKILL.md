---
name: architecture-review
description: Run a Shape-B architecture review of a target (plan file, diff, branch) against the project's architectural commitments. The skill is the API; Claude is the analyst. The dispatch endpoint loads CLAUDE.md, business-rules, recent plans, the target body, and the resolved domain's TrustedReferences; renders a structured prompt with the ARCHITECTURE_REVIEW v1 schema; Claude reads the prompt and emits the review per schema. Per BR-SKILL-012 — purely qualitative judgement; no deterministic checks (those stay in lint). Per BR-PROCESS-009 — every EXTENDS finding triggers a human-decision gate before the change can land.
argument-hint: <target> [--domain=<name>]
disable-model-invocation: true
allowed-tools: Bash(curl http://127.0.0.1:5050/skills/architecture-review/dispatch *)
---

!`curl http://127.0.0.1:5050/skills/architecture-review/dispatch -sS --max-time 5 --data-urlencode 'session_id=${CLAUDE_SESSION_ID}' --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: deterministic-helpers sidecar unreachable on 127.0.0.1:5050. Run /skill-bootstrap status, then /skill-bootstrap start.\n'`

If the helper output begins with `PRECONDITION_FAIL:`, render that exact line back to the user and stop — do not attempt this skill's actual work.

The dispatch above returned a structured prompt. Read every section it contains:

1. CLAUDE.md (the architectural commitments)
2. docs/business-rules.md (the full rule register)
3. docs/process-incidents.md (priors — failure modes the project has already named)
4. Recent plan files (last 3)
5. The resolved domain's knowledge slices (Glossary, GovernedGlobs, BusinessRulesPath, **TrustedReferences** — your citation allow-list)
6. The target body (the file under review)
7. The ARCHITECTURE_REVIEW v1 response schema

Now perform the review. Emit your response in **exactly** the schema at the bottom of the prompt. Hard requirements:

- **Every BR in scope of the change has a row** under PER-COMMITMENT EVALUATION. "In scope" means the change touches paths, behaviour, or invariants the BR governs. When in doubt, include the BR.
- **STATUS is one of: COMPATIBLE | VIOLATES | EXTENDS.** No other values.
- **Every EXTENDS row pairs with an ARCHITECTURE_DECISION_REQUIRED block** under ARCHITECTURAL DECISIONS REQUIRED. The block names the commitment, the current rule, the proposed extension, and the four options (Evolve / Constrain / Defer / Override).
- **CITED URLs come ONLY from the domain's TrustedReferences list**, or `(none)` when no external citation applies. Do NOT cite URLs that aren't in the allow-list.
- **REASONING is ≤3 sentences per row.** Tight; no narrative.
- **RECOMMENDATION is one of: PROCEED | EVOLVE_FIRST | CONSTRAIN | DEFER | DISCUSS.**

If the response doesn't match the schema (missing rows, invalid STATUS, unauthorised citation), the user will surface the mismatch and ask you to retry with the schema enforced. Don't paraphrase the schema; follow it.

The user reads your response and then resolves any ARCHITECTURE_DECISION_REQUIRED blocks per BR-PROCESS-009 (Evolve / Constrain / Defer / Override). Plan files for the change record the chosen resolution under an "Architecture review decisions" section.

Phase 2a (this commit) is the scaffolding only. Future phases integrate /architecture-review as Phase 1.5 of /extend-skills's flow (BR-PROCESS-009 gate before Phase 2 implement).
