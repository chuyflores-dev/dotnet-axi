# MVP-E10-S01 — Locate Repository Configuration

## Outcome

The tool finds at most one root-level `dotnet-axi.yml` after selecting the
workspace root.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)

## Boundary

Configuration discovery never searches parent workspaces or alternate
filenames.

## Acceptance

- Git, marker-based, and directory workspaces resolve the same root used by
  other commands.
- Missing configuration is a valid empty configuration, not an error.

## Verification

- Fixtures cover root, nested, parent-only, alternate-name, symlinked, and
  absent configuration.

## Dependencies

- `MVP-E02-S01`
