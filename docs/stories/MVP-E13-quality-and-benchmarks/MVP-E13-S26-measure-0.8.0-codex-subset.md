# MVP-E13-S26 — Measure the 0.8.0 Codex Subset

## Outcome

A manually dispatched Codex series measures the 0.8.0 safe agent-integration
task subset with the versioned agent-neutral protocol.

## Design

- [Agent-task benchmark](../../design/quality.md#agent-task-benchmark)
- [Sandboxed agent operation](../../design/agent-integration.md#sandboxed-agent-operation)

## Boundary

Results are compared only within the same exact Codex configuration, harness,
corpus, and condition exposure. They do not claim the deferred full
compatibility matrix, OpenCode setup, general source modification, or Claude
outcomes.

## Acceptance

- Every affected 0.8.0 task runs at least five times per baseline and candidate
  condition with randomized interleaving and equivalent isolated workspaces.
- Setup writes are confined to fixture-owned repository and user roots;
  passive tasks remain read-only, and no run receives ambient user
  configuration, credentials, trust state, or broader host permissions.
- The report retains complete manifests, metrics, validation, activation,
  setup effects, changed and retained targets, lifecycle, constrained-host,
  and raw-trajectory evidence and separates 0.8.0 results from earlier series.
- Safety, scope, transcript, trust, source-write, permission, stale-worker,
  cleanup, recovery-loop, regression, and completion-claim thresholds are
  evaluated even when no improvement claim is made.
- Missing, drifted, failed, timed-out, or otherwise incomparable runs remain
  explicit and cannot contribute to a release claim.

## Verification

- Normalized results reconcile with raw Codex events, task oracles, versions,
  hashes, sandbox and approval settings, setup targets, file changes, process
  evidence, and the approved 0.8.0 corpus manifest.

## Dependencies

- `MVP-E13-S18`
- `MVP-E13-S24`
- `MVP-E13-S25`
