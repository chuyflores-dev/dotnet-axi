# MVP-E10-S04 — Resolve Configured Paths

## Outcome

Configured paths resolve relative to their configuration file and cannot
silently broaden ambient workspace scope.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)
- [Paths and locations](../../design/workspace.md#paths-and-locations)

## Boundary

External paths must be explicit and remain labeled external through every
consumer.

## Acceptance

- Relative, normalized, external, missing, symlinked, and platform-specific
  paths follow one typed resolution contract.
- A path that escapes through a symlink is rejected unless explicitly scoped.

## Verification

- Cross-platform fixtures cover separators, Unicode, traversal segments,
  symlinks, external paths, missing targets, and path lists.

## Dependencies

- `MVP-E10-S03`
- `MVP-E02-S07`
