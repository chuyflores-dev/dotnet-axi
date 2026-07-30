# MVP-E02-S08 — Compute Snapshot Identity

## Outcome

Semantic operations can capture a content-derived identity for exactly the
workspace inputs they observed.

## Design

- [Snapshot identity](../../design/workspace.md#snapshot-identity)
- [Current worktree authority](../../design/foundations.md#current-worktree-authority)

## Boundary

A snapshot never claims files, frameworks, generated inputs, or configuration
that the operation did not observe.

## Acceptance

- Every documented semantic input can contribute a deterministic content
  identity.
- Relevant source, worktree, project, import, SDK, or property changes produce
  a new identity; unchanged captured scope does not.

## Verification

- Snapshot tests mutate each input class independently and compare identities
  across fresh processes.

## Dependencies

- `MVP-E02-S02`
- `MVP-E02-S03`
- `MVP-E02-S05`
- `MVP-E02-S07`
