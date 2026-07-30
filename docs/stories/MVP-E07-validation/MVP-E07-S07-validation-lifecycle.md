# MVP-E07-S07 — Control Validation Lifecycle

## Outcome

Validation honors cancellation, timeouts, dependency ordering, and
`--continue-on-error` without losing completed evidence.

## Design

- [Validation results and lifecycle](../../design/analysis-and-execution.md#validation-results-and-lifecycle)

## Boundary

Independent checks may continue only when the plan and caller allow it;
dependent checks remain skipped with a reason.

## Acceptance

- Completed, failed, skipped, cancelled, timed-out, and terminated checks stay
  distinguishable.
- Cancellation and timeout return exit `1` with the documented status and
  error code.

## Verification

- Orchestrator tests cover fail-fast, continuation, dependency skips,
  cancellation during each phase, per-check timeout, and process cleanup.

## Dependencies

- `MVP-E07-S01`
- `MVP-E08-S02`
