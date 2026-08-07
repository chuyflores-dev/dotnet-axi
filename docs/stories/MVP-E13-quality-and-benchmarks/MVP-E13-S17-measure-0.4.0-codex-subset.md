# MVP-E13-S17 — Measure the 0.4.0 Codex Subset

## Outcome

A manually dispatched Codex series measures the 0.4.0 symbol and
bounded-context task subset with the same agent-neutral protocol used for
0.3.0.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Results are compared only within the same exact Codex configuration and
harness. They are not pooled with Claude or used to claim unmeasured semantic
relationship capability.

## Acceptance

- Every affected 0.4.0 task runs at least five times per baseline and candidate
  condition with randomized interleaving and equivalent isolated workspaces.
- The report retains complete manifest, metric, validation, and raw-trajectory
  evidence and distinguishes new-task results from the 0.3.0 discovery series.
- Safety and regression thresholds are evaluated even when no improvement
  claim is made, and missing or incomparable runs remain explicit.

## Verification

- Normalized results reconcile with raw events, task oracles, versions, hashes,
  and the approved 0.4.0 corpus manifest.

## Dependencies

- `MVP-E13-S15`
- `MVP-E13-S18`
- `MVP-E13-S16`
- `MVP-E09-S14`
