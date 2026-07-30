# MVP-E02-S07 — Normalize Paths and Locations

## Outcome

All commands resolve input paths consistently and emit normalized
workspace-relative paths with one-based source locations.

## Design

- [Paths and locations](../../design/workspace.md#paths-and-locations)

## Boundary

External or symlink-escaping paths require explicit scope and remain labeled
external.

## Acceptance

- Output paths use `/` and source columns use one-based UTF-16 coordinates on
  every platform.
- Passive traversal does not follow directory symlinks by default.

## Verification

- Cross-platform path fixtures cover relative paths, separators, Unicode,
  symlinks, external files, and source coordinate conversion.

## Dependencies

- `MVP-E02-S01`
