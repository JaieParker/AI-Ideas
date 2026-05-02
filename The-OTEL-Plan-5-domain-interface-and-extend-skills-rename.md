# IDomain interface, /otel-extend → /extend-skills rename, /demo domain-arg

> Plan-5 produced as the project pivots from a single-domain (`otel`-only)
> codebase to a multi-domain shape ready to host the in-flight
> `kai-platform` domain (prototyping at `/c/Work/kai-platform`).
> Commit prefixes follow `BR-EXTEND-002`.

## Motivation

The project's self-modification flow (`/otel-extend`) and the guided
onboarding skill (`/demo`) are both *generic* in behaviour but have
the OTEL domain hardcoded into their names, their dispatch paths,
and their internal logic. With a real second domain incubating, the
hardcoding becomes a forcing function — every domain-aware feature
must either:

1. Duplicate code per domain (drift risk), or
2. Get refactored after the fact when the second domain lands.

This plan does the refactor *before* the second domain lands so the
kai-platform integration is mechanical: implement `IDomain`, register
in DI, done. No retrofit of consumers.

The chosen abstraction is a **decentralised interface** (each domain
self-implements) rather than a centralised registry. The
trade-offs were enumerated in conversation; the interface route was
selected because it:

- Aligns with the project's tier philosophy (`/skill-bootstrap`
  owns sidecar; `/otel` owns OTEL tenant; each `IDomain` impl
  owns its own knowledge surface).
- Bounds the abstraction shape to what consumers actually need.
- Enables compile-time enforcement of new-domain contract.
- Allows extension without rewrite (default-implemented members).

A second consequence: `IDomain` becomes the **knowledge facade** for
its domain. A single dispatch (`/domain-info`) can return any subset
of slices (plan-file conventions, commit prefixes, glossary,
business-rules path, etc.) in one round-trip — composing across
slices instead of forcing each consumer to invent its own retrieval.

## New / changed business rules

- **`BR-EXTEND-006` — Domains expose flow configuration via
  `IDomain`.** Each domain registers a singleton implementation of
  `IDomain` in DI. Consumers (`/extend-skills`, `/demo`, future
  `/audit`, `/glossary`) resolve domains by name through
  `IDomainResolver`. Adding a new domain is one new class plus one
  DI registration; no consumer changes.

- **`BR-EXTEND-007` — Skill names are domain-neutral when the skill
  is generic.** Self-modification flow's name is `/extend-skills`,
  not `/otel-extend`. Demo orchestrator's name is `/demo`, not
  `/demo-otel`. The first user-facing argument names the domain:
  `/extend-skills <domain> [<topic>]`, `/demo <domain>`. When a
  skill is genuinely domain-specific (e.g. a hypothetical
  `/otel set` that only OTEL has), the domain stays in the name.

- **`BR-PROCESS-001` text amendment.** References to
  `/otel-extend` rename to `/extend-skills`. Behaviour unchanged;
  the bootstrap-exception list keeps its historical entries
  (the original bootstrap commit was for `/otel-extend` *as it
  was named at the time*).

## Files affected

### New files

| Path | Purpose |
|---|---|
| `src/HelpersSidecar/Domain/IDomain.cs` | Contract — name, plan-file conventions, commit prefixes, governed globs, playbook path, glossary, business-rules path, optional default-implemented members |
| `src/HelpersSidecar/Domain/PlanFileConventions.cs` | Value object — `Pattern` (`"The-OTEL-Plan-{n}-{slug}.md"`), `NumberFloor`, slug rules |
| `src/HelpersSidecar/Domain/CommitConventions.cs` | Value object — per-phase prefix map |
| `src/HelpersSidecar/Domain/DomainHealth.cs` | Enum + record for `IDomain.Probe()` |
| `src/HelpersSidecar/Domain/OtelDomain.cs` | Concrete OTEL domain — populates every slice |
| `src/HelpersSidecar/Infrastructure/IDomainResolver.cs` | `ResolveOrThrow(name)`, `TryResolve(name, out)`, `KnownNames` |
| `src/HelpersSidecar/Infrastructure/DomainResolver.cs` | Thin wrapper over `IEnumerable<IDomain>` |
| `src/HelpersSidecar/Endpoints/DomainInfoDispatchEndpoint.cs` | `/skills/domain-info/dispatch` — accepts `domain` + `slices`, returns JSON with the requested slices only |
| `.claude/skills/extend-skills/SKILL.md` | Renamed from `.claude/skills/otel-extend/SKILL.md` |
| `.claude/skills/extend-skills/playbook.md` | Renamed from `.claude/skills/otel-extend/playbook.md`, generalised wording |
| `.claude/skills/extend-skills/phases.md` | Renamed |
| `.claude/skills/extend-skills/commit-prefixes.md` | Renamed |
| `.claude/skills/extend-skills/templates/plan-template.md` | Renamed; `{DomainName}` placeholder replaces `OTEL` literal |
| `.claude/skills/domain-info/SKILL.md` | NEW — user-only, status-only, queries domain knowledge slices |
| `.claude/skills/domain-info/HELP.md` | NEW — slice catalogue |
| `tests/HelpersSidecar.Tests/Domain/OtelDomainTests.cs` | NEW — verifies every slice on `OtelDomain` is populated and matches existing conventions |
| `tests/HelpersSidecar.Tests/Domain/DomainResolverTests.cs` | NEW — name lookup, unknown-name failure, unique-name validation |
| `tests/HelpersSidecar.Tests/Endpoints/DomainInfoDispatchTests.cs` | NEW — slice projection, multi-slice composition, unknown-domain → 404 |

### Modified files

| Path | Change |
|---|---|
| `src/HelpersSidecar/Application/OtelExtendVerb.cs` → `ExtendSkillsVerb.cs` | Rename. Args: `<domain> [<topic>]`. Default domain `otel` if absent for backward-compat. |
| `src/HelpersSidecar/Endpoints/OtelExtendDispatchEndpoint.cs` → `ExtendSkillsDispatchEndpoint.cs` | Rename. Resolves domain via `IDomainResolver`; uses `IDomain.PlanFiles` + `IDomain.Commits` for naming. Path becomes `/skills/extend-skills/dispatch`. |
| `src/HelpersSidecar/Endpoints/DemoDispatchEndpoint.cs` | Accept first argument as domain name (default `otel`). The OTEL-specific demo steps stay; future kai-platform demo will need its own step list (likely a method on `IDomain` or a separate `IDomainDemoSteps` interface). For Plan-5, only the wiring is generic. |
| `src/HelpersSidecar/Endpoints/OtelDispatchEndpoint.cs` | `extend` verb's `EXTEND_REQUESTED` marker emits `domain="otel"` explicitly. |
| `src/HelpersSidecar/Application/OtelVerb.cs` | `Extend` verb continues to take `Topic`; the chain target's args become `otel <topic>`. |
| `src/HelpersSidecar/Application/NextPlanFileName.cs` | Accepts `PlanFileConventions` instead of hardcoded `"The-OTEL-Plan"`. |
| `src/HelpersSidecar/Endpoints/NextPlanNameEndpoint.cs` | Accepts `domain` parameter; resolves conventions via `IDomainResolver`. |
| `src/HelpersSidecar/Program.cs` | DI: register `IDomain` (singleton, `OtelDomain`); register `IDomainResolver`; map `domain-info` dispatch. |
| `src/HelpersSidecar/Domain/PlanFileName.cs` | Extract pattern from a value object instead of constants. |
| `.claude/skills/otel/SKILL.md` | `allowed-tools` switches `Skill(otel-extend *)` → `Skill(extend-skills *)`. Body's chain instruction names the new skill. |
| `.claude/skills/otel-extend/` (folder) | DELETED via git mv → `extend-skills/`. |
| `tests/HelpersSidecar.Tests/Application/OtelExtendVerbTests.cs` → `ExtendSkillsVerbTests.cs` | Rename + add tests for the new `<domain>` first arg. |
| `tests/HelpersSidecar.Tests/Endpoints/OtelExtendDispatchEndpointTests.cs` → `ExtendSkillsDispatchEndpointTests.cs` | Rename + tests for resolving domain via `IDomainResolver`, unknown-domain failure path. |
| `tests/HelpersSidecar.Tests/Endpoints/DemoDispatchEndpointTests.cs` | Add tests for the `<domain>` first arg (default + explicit). |
| `tests/HelpersSidecar.Tests/Application/NextPlanFileNameTests.cs` | Re-shaped against `PlanFileConventions`. |
| `tests/HelpersSidecar.Tests/Endpoints/NextPlanNameEndpointTests.cs` | Add tests for the new `domain` parameter. |
| `tests/HelpersSidecar.Tests/SkillConventions/SkillPreconditionLintTests.cs` | Update — exemption list contains `skill-bootstrap` AND `domain-info` (or merge `domain-info` into the dispatching-skill rule via probe-or-instruct fallback — TBD inside Phase 2). |
| `docs/business-rules.md` | Add `BR-EXTEND-006`, `BR-EXTEND-007`. Amend `BR-PROCESS-001`'s text. Amend `BR-DEMO-001` to note the `<domain>` first arg. |
| `CLAUDE.md` | Rename references. New section "Domains as a first-class concept". `BR-PROCESS-001` text update. |
| `docs/process-incidents.md` | Append entry — "OTEL domain hardcoded for too long; rename + interface introduced because second domain incubating elsewhere". |

## Behavioural change

**Before:**

- `/otel-extend <topic>` — hardcoded to OTEL plan-file pattern
  (`The-OTEL-Plan-{n}-{slug}.md`), hardcoded commit prefixes, hard-
  coded governed paths.
- `/demo` — hardcoded OTEL pre-flight rows and step set.
- New domains require either renaming/forking the skills or
  embedding a switch-on-string-key everywhere.
- "What does the OTEL domain know?" requires reading multiple files.

**After:**

- `/extend-skills <domain> [<topic>]` — generic flow; the domain
  argument resolves to an `IDomain` and the flow uses that
  domain's `PlanFiles`, `Commits`, `GovernedGlobs`. No code path
  knows about OTEL specifically.
- `/demo <domain>` — generic harness; for Plan-5 the OTEL domain
  is the only one with a populated demo. (Future plan: extract
  demo steps to `IDomain` or a parallel `IDomainDemo`.)
- `/domain-info <domain> [slices]` — query the domain's knowledge
  facade; returns the requested slices in one response.
- `/otel extend <topic>` — chains to `/extend-skills otel <topic>`
  via the existing `EXTEND_REQUESTED` marker (just renamed).
- New domains plug in by adding `class FooDomain : IDomain` and
  one DI registration.

## Test approach

Per `BR-PROCESS-007` every test scopes to one domain change:

- **`OtelDomainTests`** — verifies every slice on `OtelDomain` is
  populated AND matches the existing repo conventions (e.g.
  `PlanFiles.Pattern == "The-OTEL-Plan-{n}-{slug}.md"`, commit
  prefixes match `BR-EXTEND-002`, governed globs match
  `BR-PROCESS-001`). The test catches drift if someone changes a
  convention without updating the domain.
- **`DomainResolverTests`** — name-based lookup, unknown-name
  throws, and a uniqueness check (no two registered `IDomain`
  share a `Name`).
- **`DomainInfoDispatchTests`** — slice projection works for any
  subset (`slices=PlanFiles`, `slices=Glossary,Probe`,
  `slices=*`); unknown domain returns 404; unknown slice name
  returns 400.
- **`ExtendSkillsDispatchEndpointTests`** — the existing
  `OtelExtendDispatchEndpointTests` re-shaped: every test now
  passes `domain=otel` and asserts via `IDomainResolver`. Adds a
  test for unknown-domain failure path.
- **`NextPlanFileNameTests`** — re-shaped against
  `PlanFileConventions`. The pattern is now data, not a constant.
- **`SkillPreconditionLintTests`** — exemption list grows by one
  entry (`domain-info`) IF that skill is read-only and OTEL-
  independent. (Phase 2 decides whether `/domain-info` follows
  the probe-or-instruct rule via the regular dispatch path or
  becomes a second exemption alongside `/skill-bootstrap`.)
- **No `/demo` test changes for the kai-platform path** — that
  domain isn't here yet. The `/demo otel` path stays the same
  shape it was after Plan-4.

Loop discipline holds: every new test mocks at the
`IDomainResolver` seam (or constructs an `OtelDomain` directly
where the seam isn't relevant). Cross-domain tests would be
opt-in via `[Trait("Scope","cross-domain")]` per `BR-PROCESS-007`
but none are added in this plan.

## Phase ordering

1. **Phase 1 (this commit)** — plan file. `plan:` prefix.

2. **Phase 2a** — Introduce `IDomain` + `OtelDomain` +
   `IDomainResolver` + `DomainResolver` + tests. **No consumers
   yet.** The DI registration lands; nothing else uses it.
   `feat(otel):` prefix. (Justification: pure addition. The
   interface is born with one implementation; consumer wiring
   follows.)

3. **Phase 2b** — Wire `IDomainResolver` into the existing
   `OtelExtendDispatchEndpoint` and `NextPlanNameEndpoint`. The
   dispatch path is still `/skills/otel-extend/dispatch`; only
   the internal lookup changes. Tests still pass.
   `refactor(otel):` prefix.

4. **Phase 2c** — Rename. `git mv .claude/skills/otel-extend
   .claude/skills/extend-skills`; rename C# files via class +
   namespace updates; update dispatch path; update `/otel`'s
   chain marker; update `allowed-tools` patterns. The
   `<domain>` first arg becomes required (default `otel` if
   omitted, for one-cycle backward compat — to be removed in
   a follow-up plan). `refactor(skills):` prefix.

5. **Phase 2d** — `/demo` accepts `<domain>` first arg. Default
   `otel`. Existing demo body is the OTEL domain's demo;
   `IDomain` does NOT carry demo steps in this plan (deferred —
   needs a second example to design well). `refactor(otel):`
   prefix.

6. **Phase 2e** — `/domain-info` skill + dispatch endpoint.
   Slice projection logic. `feat(domain-info):` prefix.

7. **Phase 2f** — Documentation: `CLAUDE.md` rewrite of
   self-modification section to use `/extend-skills` naming;
   add "Domains as a first-class concept" section;
   `docs/business-rules.md` adds `BR-EXTEND-006`,
   `BR-EXTEND-007`, amends `BR-PROCESS-001`,
   `BR-DEMO-001`; `docs/process-incidents.md` appends entry.
   `docs:` prefix.

8. **Phase 3 — Build.** `dotnet build`; `chore:` prefix only if
   artefacts changed.

9. **Phase 4 — Test.** Full suite + lint + manual smoke of
   `/extend-skills otel domain-interface-self-host` (a no-op
   topic just to verify the chain works post-rename). `test:`
   prefix.

10. **Phase 5 (acceptance, optional)** — re-run `/demo otel` to
    confirm 14/14 PASS. Not a commit; a sanity check.

## Rollback

Each phase commits separately; revert any individually:

1. `git revert <plan-commit-sha>` — drops Plan-5.
2. `git revert <2a..2f-commit-shas>` (in reverse order) — drops
   the implementation.
3. `git revert <3-chore-sha>` — drops rebuilt artefacts.
4. `git revert <4-test-sha>` — drops the test pass marker.

Reverting Phase 2c specifically (the rename) restores
`/otel-extend` as the canonical name; the interface introduced in
2a + 2b stays as latent infrastructure.

## Out of scope

- **kai-platform domain implementation.** It lives in
  `/c/Work/kai-platform` and is not yet ready to land in this
  repo. Plan-5 prepares the contract; Plan-6+ (whenever
  kai-platform is ready) will add `KaiPlatformDomain : IDomain`
  and any consumer-side changes its specifics demand.
- **Demo steps as a domain slice.** `IDomain` does not yet expose
  a demo-step contract; the OTEL demo's 14 steps stay
  hardcoded inside `DemoDispatchEndpoint`. Designing
  `IDomainDemo` against one example would be premature
  (`BR-PROCESS-005` evidence rule). Revisit when
  kai-platform's demo shape is concrete.
- **Backward-compat shim for `/otel-extend`.** The default
  domain `otel` covers the case where the chain from `/otel
  extend` calls without specifying domain; we don't add a
  separate alias for the old skill name. Anyone scripting
  against `/otel-extend` directly will need to update.
- **`/skill-bootstrap` participation in the domain interface.**
  The platform skill is intentionally domain-independent; it
  stays out of this refactor.
- **Multi-domain audit / cross-domain analysis tools.** The
  interface enables them; we don't build them here. Each is its
  own future plan if the value materialises.
- **Domain registration through configuration (rather than
  DI).** Future option. Today, registration is a DI line in
  `Program.cs`; that's a single source of truth and is enough.

## What kai-platform plugging in will look like

For reference (NOT in scope of this plan):

```csharp
public sealed class KaiPlatformDomain : IDomain
{
    public string Name => "kai-platform";
    public PlanFileConventions PlanFiles => new(
        Pattern: "The-KaiPlatform-Plan-{n}-{slug}.md",
        NumberFloor: 1);
    public CommitConventions Commits => new(/* per-phase prefixes */);
    public IReadOnlyList<string> GovernedGlobs => new[]
    {
        "src/KaiPlatform/**",
        // ...
    };
    public string PlaybookPath => "kai-platform-playbook.md";
    public IReadOnlyDictionary<string, string> Glossary { get; } =
        new Dictionary<string, string>
        {
            // kai-platform terms
        };
    // ...
}
```

Plus one line in `Program.cs`:

```csharp
builder.Services.AddSingleton<IDomain, KaiPlatformDomain>();
```

Zero changes to `/extend-skills`, `/demo`, `/domain-info`, or any
existing OTEL code. That's the point of the plan.
