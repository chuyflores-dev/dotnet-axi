# MVP-E13-S03 — Add Worktree and Failure Fixtures

## Outcome

The fixture catalog represents Git changes, ambiguity, missing dependencies,
unsupported inputs, external imports, and conflicts.

## Design

- [Integration fixtures](../../design/quality.md#integration-fixtures)

## Boundary

Each fixture isolates one edge condition or a documented interaction required
to reproduce behavior.

## Acceptance

- Staged, unstaged, untracked, renamed, deleted, conflicted, ambiguous
  solution, broken project, missing assets, and external-import states are
  reproducible.
- Manifests state which failures are intentional and what coverage remains.

## Verification

- Catalog self-tests inspect Git and filesystem state and reject drift from the
  committed manifests.

## Dependencies

- `MVP-E13-S01`
