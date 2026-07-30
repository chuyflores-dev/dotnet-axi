# MVP-E02-S01 — Discover the Workspace

## Outcome

The tool discovers the current workspace root and catalogs supported solution,
project, and configuration markers without evaluating the full project graph.

## Design

- [Repository discovery](../../design/workspace.md#repository-discovery)
- [Solution and project selection](../../design/workspace.md#solution-and-project-selection)

## Boundary

Discovery reports solution filters and unsupported entry points as
capabilities; it does not silently select them as fully supported.

## Acceptance

- Git and non-Git root precedence matches the design.
- `.sln`, `.slnx`, supported projects, and relevant root markers are returned
  in deterministic order.

## Verification

- Fixture tests cover Git roots, nested directories, configured roots, single
  projects, and marker-free directories.

## Dependencies

- `MVP-E01-S03`
