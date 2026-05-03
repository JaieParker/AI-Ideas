# /domain-info command reference

Read-only query over a domain's knowledge slices.

```text
/domain-info <domain>                  return every slice
/domain-info <domain> <slices>         return just the listed slices (comma-separated)
```

## Slice catalogue

| slice                  | what it returns                                  | BR              |
|------------------------|--------------------------------------------------|-----------------|
| `name`                 | the domain's stable identifier (e.g. `otel`)     | BR-EXTEND-006   |
| `plan-files`           | `{prefix, number_floor, suffix}`                 | BR-EXTEND-004/006 |
| `commits`              | per-phase commit-message prefixes                | BR-EXTEND-002   |
| `governed-globs`       | path patterns the extend flow governs            | BR-PROCESS-001  |
| `playbook-path`        | flow playbook (relative path)                    | BR-EXTEND-006   |
| `glossary`             | ubiquitous-language terms                        | BR-EXTEND-006   |
| `business-rules-path`  | BR document for this domain                      | BR-EXTEND-006   |
| `trusted-references`   | curated authoritative external sources           | BR-EXTEND-008   |
| `all`                  | every slice (default when no slices arg)         |                 |

## Examples

```
/domain-info otel
/domain-info otel glossary
/domain-info otel trusted-references,plan-files
/domain-info otel commits,governed-globs
```

## Forward-compat

When kai-platform lands as a registered `IDomain`, the same
verbs apply: `/domain-info kai-platform`, etc. The slice names
are the contract.
