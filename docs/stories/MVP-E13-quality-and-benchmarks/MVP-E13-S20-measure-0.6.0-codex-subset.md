# MVP-E13-S20 — Measure the 0.6.0 Codex Subset

## Outcome

A manually dispatched Codex series measures the 0.6.0 semantic-relationship
and graph task subset with the versioned agent-neutral protocol.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Results are compared only within the same exact Codex configuration, harness,
corpus, and condition exposure. They are not pooled with Claude or used to
claim unmeasured analysis, validation, or mutation capability.

## Acceptance

- Every affected 0.6.0 task runs at least five times per baseline and candidate
  condition with randomized interleaving and equivalent isolated workspaces.
- The report retains complete manifests, metrics, validation, activation, and
  raw-trajectory evidence and separates 0.6.0 results from earlier series.
- Safety, scope, activation, and regression thresholds are evaluated even when
  no improvement claim is made, and missing or incomparable runs remain
  explicit.
- Exact-fact-set, inspected-scope, and candidate-activation reconciliation use
  the corrected versioned protocol without rewriting retained earlier series.

## Verification

- Normalized results reconcile with raw events, task oracles, versions,
  hashes, activation evidence, and the approved 0.6.0 corpus manifest.

## Dependencies

- `MVP-E13-S17`
- `MVP-E13-S18`
- `MVP-E13-S19`
