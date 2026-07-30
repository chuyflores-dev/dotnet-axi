# MVP-E02 — Workspace Discovery and Snapshot Identity

## Outcome

Every workspace-aware command resolves an explicit repository, solution,
project, framework, changed scope, and content-derived snapshot.

## Scope

- Repository and entry-point discovery with deterministic ambiguity handling.
- Git worktree and changed-scope interpretation.
- MSBuild project evaluation, multi-targeting, unsupported inputs, and broken
  project coverage.
- Normalized paths, locations, ownership, and snapshot identity.

## Boundary

Code relationship and impact traversal belong to `MVP-E05`; workspace
discovery must not perform implicit restore or compilation.

## Design

- [Workspace](../../design/workspace.md)
- [Design foundations](../../design/foundations.md)
- [Runtime and distribution](../../design/runtime-and-distribution.md)

## Dependencies

- `MVP-E01`

## Stories

- [MVP-E02-S01 — Discover the workspace](MVP-E02-S01-workspace-discovery.md)
- [MVP-E02-S02 — Select the workspace entry point](MVP-E02-S02-workspace-selection.md)
- [MVP-E02-S03 — Report worktree state](MVP-E02-S03-worktree-state.md)
- [MVP-E02-S04 — Resolve changed scope](MVP-E02-S04-changed-scope.md)
- [MVP-E02-S05 — Evaluate the MSBuild project graph](MVP-E02-S05-msbuild-project-graph.md)
- [MVP-E02-S06 — Report project and framework coverage](MVP-E02-S06-project-coverage.md)
- [MVP-E02-S07 — Normalize paths and locations](MVP-E02-S07-paths-and-locations.md)
- [MVP-E02-S08 — Compute snapshot identity](MVP-E02-S08-snapshot-identity.md)

## Complete when

- Supported and degraded workspace states are deterministic, structured, and
  actionable across fresh processes.
- Results never omit unresolved projects or claim scope that the snapshot did
  not observe.
