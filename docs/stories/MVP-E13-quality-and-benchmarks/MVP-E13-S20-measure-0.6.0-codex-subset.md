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

- Every affected 0.6.0 task has one matched baseline and candidate run by
  default, using equivalent isolated workspaces. Broader representative task
  coverage takes priority over repeated runs of a small synthetic subset.
- A formal comparative claim may repeat the complete matched suite for added
  confidence. It retains every original run, including timeouts and startup
  failures, and never reruns or discards only unfavorable cases.
- The report retains the run manifest, metrics, deterministic validation,
  activation observations, and raw events; it separates 0.6.0 results from
  earlier series and leaves missing or incomparable runs explicit.
- Safety and scope remain correctness gates. Activation and recovered command
  diagnostics are retained observations and do not replace deterministic task
  validation.

## Verification

- Normalized results reconcile with raw events, task oracles, versions,
  hashes, activation evidence, and the approved 0.6.0 corpus manifest.

## Dependencies

- `MVP-E13-S17`
- `MVP-E13-S18`
- `MVP-E13-S19`
