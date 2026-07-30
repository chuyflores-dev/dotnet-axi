# MVP-E06 — Static Analysis

## Outcome

Agents can obtain normalized compiler, configured analyzer, structural, and
architecture findings for selected or changed scope.

## Scope

- Compiler diagnostics and configured analyzer execution.
- Structural rules and basic architecture rules.
- Changed-scope analysis and affected project selection.
- Normalized findings, duplicate provenance, failures, cancellation, and
  coverage.

## Boundary

Validation profile orchestration belongs to `MVP-E07`; arbitrary analyzer-pack
installation is not part of the MVP.

## Design

- [Analysis and execution](../../design/analysis-and-execution.md)
- [Design foundations](../../design/foundations.md)
- [Semantics and graph](../../design/semantics-and-graph.md)

## Dependencies

- `MVP-E02`
- `MVP-E03`
- `MVP-E05`

## Stories

- [MVP-E06-S01 — Define finding contracts](MVP-E06-S01-finding-contracts.md)
- [MVP-E06-S02 — Analyze compiler diagnostics](MVP-E06-S02-compiler-analysis.md)
- [MVP-E06-S03 — Run configured analyzers](MVP-E06-S03-configured-analyzers.md)
- [MVP-E06-S04 — Run structural rules](MVP-E06-S04-structural-analysis.md)
- [MVP-E06-S05 — Run architecture rules](MVP-E06-S05-architecture-analysis.md)
- [MVP-E06-S06 — Analyze changed scope](MVP-E06-S06-changed-analysis.md)
- [MVP-E06-S07 — Contain analyzer failures](MVP-E06-S07-analyzer-failure-containment.md)
- [MVP-E06-S08 — Merge finding provenance](MVP-E06-S08-finding-provenance.md)

## Complete when

- Each analysis mode reports stable findings and the exact scope it could
  analyze.
- Analyzer, generator, rule, or project failures cannot crash the CLI or be
  reported as successful complete analysis.
