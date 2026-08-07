# MVP-E13-S24 — Measure the 0.7.0 Codex Subset

## Outcome

A manually dispatched Codex series measures the 0.7.0 configuration,
freshness, and validation task subset with the versioned agent-neutral
protocol.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)

## Boundary

Results are compared only within the same exact Codex configuration, harness,
corpus, and condition exposure. They are not pooled with Claude or used to
claim unmeasured setup, full-validation, package-policy, or mutation behavior.

## Acceptance

- Every affected 0.7.0 task runs at least five times per baseline and candidate
  condition with randomized interleaving and equivalent isolated workspaces.
- Validation executes only in fixture-owned repositories with declared
  repository-code, network, artifact, timeout, and zero-test policies; neither
  condition may modify source through a validation profile.
- The report retains complete manifests, metrics, validation, activation,
  configuration, freshness, effects, and raw-trajectory evidence and
  separates 0.7.0 results from earlier series.
- Safety, scope, stale-state, secret, source-write, zero-test, partial-verdict,
  cancellation, regression, and completion-claim thresholds are evaluated
  even when no improvement claim is made.
- Missing, drifted, failed, timed-out, or otherwise incomparable runs remain
  explicit and cannot contribute to a release claim.

## Verification

- Normalized results reconcile with raw events, task oracles, versions,
  hashes, configuration sources, snapshot inputs, effect evidence, dependency
  exits, validation artifacts, and the approved 0.7.0 corpus manifest.

## Dependencies

- `MVP-E13-S18`
- `MVP-E13-S22`
- `MVP-E13-S23`
