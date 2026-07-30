# MVP-E06-S02 — Analyze Compiler Diagnostics

## Outcome

`analyze compiler` returns Roslyn compiler diagnostics for the selected
document, project, affected, or solution scope.

## Design

- [Compiler diagnostics](../../design/analysis-and-execution.md#compiler-diagnostics)

## Boundary

Compiler analysis does not run configured analyzers or source generators
without explicit executing consent.

## Acceptance

- Diagnostics retain code, severity, message, normalized location, project,
  framework, and exact analyzed scope.
- Broken or unsupported projects remain visible and prevent false complete
  coverage.

## Verification

- Roslyn fixtures cover syntax and semantic diagnostics, multi-targeting,
  linked files, suppression, broken projects, and partial scope.

## Dependencies

- `MVP-E06-S01`
- `MVP-E02-S06`
- `MVP-E04-S04`
