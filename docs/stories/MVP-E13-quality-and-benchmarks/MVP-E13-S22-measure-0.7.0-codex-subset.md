# MVP-E13-S22 — Measure the 0.7.0 Codex Subset

## Outcome

A manually dispatched Codex series measures the 0.7.0 analysis and SDK
execution task subset with the versioned agent-neutral protocol.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Results are compared only within the same exact Codex configuration, harness,
corpus, and condition exposure. They are not pooled with Claude or used to
claim unmeasured validation, setup, or mutation capability.

## Acceptance

- Every affected 0.7.0 task runs at least five times per baseline and candidate
  condition with randomized interleaving and equivalent isolated workspaces.
- Executing tasks use only fixture-owned repositories, dependency stores,
  artifacts, temporary paths, and explicitly declared offline or controlled
  network conditions.
- The report retains complete manifests, metrics, validation, activation,
  effects, and raw-trajectory evidence and separates 0.7.0 results from
  earlier series.
- Safety, scope, activation, regression, source-write, and child-process
  thresholds are evaluated even when no improvement claim is made, and missing
  or incomparable runs remain explicit.

## Verification

- Normalized results reconcile with raw events, task oracles, versions,
  hashes, activation and effect evidence, dependency exits, and the approved
  0.7.0 corpus manifest.

## Dependencies

- `MVP-E13-S18`
- `MVP-E13-S20`
- `MVP-E13-S21`
