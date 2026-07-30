# MVP-E11-S06 — Create Isolated Diagnostic Artifacts

## Outcome

Raw logs and diagnostic artifacts are created outside the repository in
randomized tool-owned directories with restrictive permissions.

## Design

- [Diagnostic artifacts](../../design/runtime-and-distribution.md#diagnostic-artifacts)

## Boundary

Binary logs are opt-in, and artifacts default to
`may_contain_sensitive_data: true`.

## Acceptance

- Artifact metadata records type, path, sensitivity, retention, size, and
  creation result without embedding raw content in normal stdout.
- Creation rejects unsafe parent state and applies user-only permissions where
  the platform supports them.

## Verification

- Filesystem tests cover repository location avoidance, randomized names,
  permissions, failures, concurrent creation, sensitivity labels, and binary
  log opt-in.

## Dependencies

- `MVP-E11-S05`
