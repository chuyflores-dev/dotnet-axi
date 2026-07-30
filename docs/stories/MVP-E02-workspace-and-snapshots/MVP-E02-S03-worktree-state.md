# MVP-E02-S03 — Report Worktree State

## Outcome

Workspace results expose tracked, staged, unstaged, untracked, renamed,
deleted, and conflicted Git state.

## Design

- [Worktree awareness](../../design/workspace.md#worktree-awareness)

## Boundary

Non-Git workspaces remain valid; only Git-specific requests require Git.

## Acceptance

- Current worktree categories and branch state are represented without losing
  renames or deletions.
- Conflicted files are identified so read and mutation consumers can apply
  their respective boundaries.

## Verification

- Git fixture tests cover every worktree category, conflicts, detached HEAD,
  and a workspace without Git.

## Dependencies

- `MVP-E02-S01`
