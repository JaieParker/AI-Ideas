# /skill-bootstrap command reference

Bootstrap and lifecycle for the .NET deterministic-helpers sidecar
— the platform every other skill in this project routes through.
This skill is **OTEL-independent**: it does not check the collector,
does not assume OTEL is on, and does not crash when OTEL is off.

```text
# /skill-bootstrap — bootstrap and lifecycle of :5050

/skill-bootstrap                   status table only (no side effects)
/skill-bootstrap install           dotnet build src/HelpersSidecar
/skill-bootstrap start             spawn sidecar in background, poll healthz
/skill-bootstrap stop              terminate process(es) listening on :5050
```

## Pre-requirements probed on every invocation

| # | requirement                          | how it's checked
|---|--------------------------------------|-----------------
| 1 | .NET 10 SDK on PATH                   | `dotnet --version`
| 2 | sidecar source present                | glob `src/HelpersSidecar/HelpersSidecar.csproj`
| 3 | sidecar built                         | glob `src/HelpersSidecar/bin/Debug/net10.0/HelpersSidecar.dll`
| 4 | port 5050 free or owned by sidecar    | `curl :5050/healthz` + `Get-NetTCPConnection`
| 5 | sidecar healthz                       | `curl http://127.0.0.1:5050/healthz`

## Independence from /otel

This skill manages the **deterministic-helpers platform** — the
.NET sidecar that hosts every skill's dispatch endpoint. The Go
collector (today) and the .NET collector (post-pivot) are tenants
on top of that platform; their lifecycle is owned by `/otel up`
and `/otel down` (collector tier), not by this skill.

The pivot to a .NET-only collector will fold collector lifecycle
into this skill (one bootstrap, both tiers, one language). Until
that lands, `/skill-bootstrap` covers the sidecar tier only.

## Why this skill is the only `!`-line that doesn't dispatch via :5050

Every other skill's `!` preprocessing line is a `curl` to
`http://127.0.0.1:5050/skills/<name>/dispatch`. If `:5050` is not
listening, those skills fail at the `!` stage and their body never
reaches Claude. `/skill-bootstrap` exists precisely to fix that
state, so it can't depend on the very thing it's supposed to bring
up. Its `!` line probes `:5050/healthz` directly with a `||`
fallback that always returns exit 0, so the skill body always
reaches Claude with a usable signal.

This is the only such exemption in the project. `BR-SKILL-010`
(landed in a later phase via `/otel-extend`) lints every other
skill to enforce the probe-or-instruct fallback, with this skill
as the named exemption.
