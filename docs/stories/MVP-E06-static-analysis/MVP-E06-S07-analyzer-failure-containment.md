# MVP-E06-S07 — Contain Analyzer Failures

## Outcome

Analyzer and source-generator crashes, load failures, and timeouts remain
isolated structured findings or coverage failures.

## Design

- [Configured analyzers](../../design/analysis-and-execution.md#configured-analyzers)

## Boundary

A failed component cannot corrupt another workspace snapshot or be reported as
successful analysis.

## Acceptance

- Each failure identifies the component, affected scope, lifecycle state, and
  actionable diagnostic evidence.
- Later independent components can continue when policy permits.

## Verification

- Hostile analyzer fixtures cover throw, crash, hang, load failure, malformed
  output, cancellation, and repeated clean execution.

## Dependencies

- `MVP-E06-S03`
- `MVP-E08-S02`
