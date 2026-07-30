# MVP-E11-S02 — Enforce Passive Operation Boundaries

## Outcome

Passive commands cannot initiate restore, telemetry, network access,
repository-code execution, configured analyzers, or source generators.

## Design

- [Network and telemetry](../../design/runtime-and-distribution.md#network-and-telemetry)
- [Repository-code execution](../../design/runtime-and-distribution.md#repository-code-execution)

## Boundary

Missing assets or generated code reduce coverage and provide a correction
instead of escalating implicitly.

## Acceptance

- Passive services receive no executing dependencies except guarded interfaces
  that reject invocation.
- Home, workspace catalog, file/text/syntax search, and passive semantics stay
  passive in degraded repositories.

## Verification

- Integration monitors fail on process, network, restore, analyzer, generator,
  telemetry, or source-write activity during representative passive commands.

## Dependencies

- `MVP-E11-S01`
- `MVP-E01-S07`
- `MVP-E03-S03`
- `MVP-E03-S08`
