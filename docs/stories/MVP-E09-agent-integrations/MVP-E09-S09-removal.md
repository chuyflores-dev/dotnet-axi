# MVP-E09-S09 — Remove Integrations

## Outcome

Setup removal deletes only tool-owned integration entries in the explicitly
selected repository or user scope.

## Design

- [Explicit setup](../../design/agent-integration.md#explicit-setup)
- [Path and configuration safety](../../design/agent-integration.md#path-and-configuration-safety)

## Boundary

Removal preserves unrelated configuration, hooks, skills, backups, trust
state, managed policy, and entries in the other scope.

## Acceptance

- Every removed and retained target is reported before and after the operation.
- Repeated removal is a no-op, and partial failure leaves recoverable
  configuration.

## Verification

- Lifecycle fixtures cover repository/user scope, mixed ownership,
  customization, concurrent edits, partial failure, backup recovery, and
  repeated removal.

## Dependencies

- `MVP-E09-S03`
- `MVP-E09-S04`
- `MVP-E09-S05`
- `MVP-E09-S07`
