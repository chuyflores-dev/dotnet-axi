# MVP-E11-S13 — Stabilize Process-group Termination

## Outcome

POSIX timeout cleanup signals each owned process group at most once while
retaining bounded descendant containment and exact exit evidence.

## Design

- [Process and secret safety](../../design/runtime-and-distribution.md#process-and-secret-safety)
- [Constrained host failures](../../design/runtime-and-distribution.md#constrained-host-failures)

## Boundary

This story changes neither the portable process-group containment guarantee nor
the typed failure returned when termination cannot be confirmed.

## Acceptance

- A timeout or cancellation path and the concurrent exit-observation path do
  not repeat a successful process-group termination request.
- A failed termination request remains retryable while termination authority
  is retained.
- Cleanup remains bounded and reports terminated only after the owned group,
  leader exit evidence, and redirected output handles are complete.

## Verification

- Deterministic unit coverage proves external termination is not repeated by
  exit observation, while adversarial descendant timeout coverage passes in
  the canonical suite.

## Dependencies

- `MVP-E11-S04`
- `MVP-E11-S12`
