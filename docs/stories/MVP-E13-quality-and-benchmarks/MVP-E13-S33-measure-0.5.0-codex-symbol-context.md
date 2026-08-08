# MVP-E13-S33 — Measure the 0.5.0 Codex Symbol-context Subset

## Outcome

A manually dispatched Codex series measures the 0.5.0 symbol and
bounded-context task subset with the same agent-neutral protocol used by the
earlier discovery series.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Results are compared only within the same exact Codex configuration and
harness. They are not pooled with Claude or used to claim unmeasured semantic
relationship capability.

## Acceptance

- Every affected 0.5.0 task runs at least five times per baseline and
  candidate condition with randomized interleaving and equivalent isolated
  workspaces.
- The report retains complete manifest, metric, activation, validation, and
  raw-trajectory evidence and distinguishes new-task results from the 0.4.0
  dnx-first discovery series.
- Safety and regression thresholds are evaluated even when no improvement
  claim is made, and missing or incomparable runs remain explicit.

## Verification

- Normalized results reconcile with raw events, task oracles, versions,
  hashes, command activation, and the approved 0.5.0 corpus manifest.

## Dependencies

- `MVP-E13-S16`
- `MVP-E13-S17`
- `MVP-E09-S13`
