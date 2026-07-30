# MVP-E03-S01 — Traverse Source Consistently

## Outcome

File, text, and structural engines receive the same deterministic set of
eligible workspace paths.

## Design

- [Shared traversal](../../design/search-and-context.md#shared-traversal)

## Boundary

Backend-specific ignore files and parent or user-global rules never broaden or
narrow the tool-owned traversal contract.

## Acceptance

- Git ignores, repository configuration, generated-code policy, build-output
  exclusions, explicit path scope, and symlink policy are applied once.
- Optional engines can consume the resulting path set without applying hidden
  traversal defaults.

## Verification

- Traversal fixtures cover ignored, hidden, generated, build, symlinked,
  external, and explicitly included paths.

## Dependencies

- `MVP-E02-S01`
- `MVP-E02-S07`
