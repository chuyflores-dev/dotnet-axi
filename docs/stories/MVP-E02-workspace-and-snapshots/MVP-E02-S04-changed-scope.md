# MVP-E02-S04 — Resolve Changed Scope

## Outcome

`--changed`, `--base`, and `--head` resolve a precise committed and worktree
change set.

## Design

- [Worktree awareness](../../design/workspace.md#worktree-awareness)

## Boundary

This story returns changed paths and resolved commits; affected semantic scope
belongs to later analysis stories.

## Acceptance

- Default, merge-base-plus-worktree, and committed three-dot modes match the
  documented semantics.
- Results state resolved commits and whether ambient worktree changes were
  included.

## Verification

- Git graph fixtures cover each flag combination, renames, deletions, invalid
  refs, and non-Git usage.

## Dependencies

- `MVP-E02-S03`
- `MVP-E01-S05`
