# MVP-E02-S09 - Stabilize host-runner timeout cleanup

## Outcome

Host authority timeout verification is deterministic while cleanup remains bounded for cooperative and uncooperative process operations.

## Design

- [Evaluated project graph](../../design/workspace.md#evaluated-project-graph)
- [Constrained host failures](../../design/runtime-and-distribution.md#constrained-host-failures)

## Boundary

This story does not change timeout values, result contracts, or process-containment policy.

## Acceptance

- Cancellation verification synchronizes with the underlying reader instead of depending on continuation scheduling.
- Timeout, output-limit, caller-cancellation, and process-tree containment behavior remains covered.
- Shared failure cleanup has one implementation path.

## Verification

- Repeated focused execution covers the former CI failure.
- Canonical verification and the independent-review gate pass.

## Dependencies

- `MVP-E02-S05`
