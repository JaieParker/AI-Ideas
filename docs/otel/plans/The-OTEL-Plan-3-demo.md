# /demo skill — orchestrates the 15-step end-to-end demo

> Plan-3 produced by `/otel-extend` Phase 1.

## Motivation

We have all the pieces (collector, enrichment, helpers, skills) but
no single user-invocable command that runs the full demo. `/demo`
fills that gap: one slash command, 15 steps run server-side, output
formatted for the prompt.

## Files affected

| Path | Change |
|---|---|
| `.claude/skills/demo/SKILL.md` | NEW — user-invocable, single curl to the dispatch endpoint |
| `src/HelpersSidecar/Endpoints/DemoDispatchEndpoint.cs` | NEW — runs the 15 steps using the existing `ICollectorControlClient` plus a small in-process OTLP-trace sender; returns multi-line text |
| `tests/HelpersSidecar.Tests/Endpoints/DemoDispatchEndpointTests.cs` | NEW — drives the endpoint with a fake collector; asserts the orchestration text |
| `src/HelpersSidecar/Program.cs` | EDIT — wire `MapDemoDispatch` |

## Behavioural change

**Before:** the 15-step demo lives in `tests/integration/collector_smoke.sh`. To
run it the user shells out and follows along. Not a skill.

**After:** `/demo` invocation runs the same flow inside the sidecar
and returns the step-by-step output for Claude to render.

## Test approach

`DemoDispatchEndpointTests.cs` injects a fake `ICollectorControlClient`
that records each call. Assertions verify the 15 steps fire in order
and the response text contains the expected step markers.

## Rollback

Each phase commits separately; revert any individually.

## Out of scope

- The actual OTLP-record-sending step uses our HTTP loopback to the
  collector's `:4318`. If the collector isn't running, the demo
  reports it gracefully — same pattern as other skills.
- No retry / progress streaming; one shot, returns when done.
