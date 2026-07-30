# MVP-E11-S07 — Defend Filesystem Writes

## Outcome

Tool-owned artifact, backup, and configuration writes reject symlink,
reparse-point, path-swap, and partial-write substitution.

## Design

- [Diagnostic artifacts](../../design/runtime-and-distribution.md#diagnostic-artifacts)
- [Setup](../../design/runtime-and-distribution.md#setup)

## Boundary

This story supplies reusable safe-write primitives; source-write authorization
belongs to `MVP-E11-S08`.

## Acceptance

- Parent and target identities are checked immediately before atomic creation
  or replacement.
- Unsafe substitution, permission failure, and interrupted replacement leave
  existing user data intact or recoverable.

## Verification

- Race-oriented fixtures cover symlinks, reparse points, path swaps, existing
  targets, permissions, interruption, backup recovery, and unsupported
  platform primitives.

## Dependencies

- `MVP-E11-S06`
