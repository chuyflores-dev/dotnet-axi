# MVP-E07-S08 — Summarize Validation Evidence

## Outcome

Validation returns precomputed counts, durations, top failures, analyzed scope,
and protected diagnostic-artifact references.

## Design

- [Validation results and lifecycle](../../design/analysis-and-execution.md#validation-results-and-lifecycle)
- [Diagnostic artifacts](../../design/runtime-and-distribution.md#diagnostic-artifacts)

## Boundary

Raw SDK, test, and analyzer logs remain outside normal stdout.

## Acceptance

- Overall status and passed, failed, skipped, warning, duration, scope, and top
  failure summaries agree with individual checks.
- Raw evidence is retrievable through a structured protected artifact
  reference.

## Verification

- Golden tests cover small and large result sets, partial runs, failures,
  cancellation, artifact presence, and output budgets.

## Dependencies

- `MVP-E07-S01`
- `MVP-E07-S07`
- `MVP-E11-S06`
