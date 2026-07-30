# MVP-E07-S01 — Define Validation Contracts

## Outcome

Validation checks and profiles use typed plans, lifecycle states, results, and
aggregate verdicts.

## Design

- [Validation results and lifecycle](../../design/analysis-and-execution.md#validation-results-and-lifecycle)

## Boundary

Individual analysis and SDK adapters remain owned by their feature epics.

## Acceptance

- A check declares scope, dependencies, side effects, timeout, and result
  translation.
- Aggregate status can represent pass, fail, warning, skipped, cancelled, and
  unavailable checks without ambiguity.

## Verification

- Contract tests cover valid plans, dependency order, lifecycle transitions,
  and aggregate exit mapping.

## Dependencies

- `MVP-E01-S03`
- `MVP-E01-S05`
