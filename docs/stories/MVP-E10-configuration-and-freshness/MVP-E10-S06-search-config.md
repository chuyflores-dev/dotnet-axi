# MVP-E10-S06 — Bind Search Configuration

## Outcome

Search and structural commands consume validated exclusions, generated-code
defaults, limits, and rule directories.

## Design

- [Repository configuration](../../design/runtime-and-distribution.md#repository-configuration)
- [Shared traversal](../../design/search-and-context.md#shared-traversal)

## Boundary

Configuration affects stable tool behavior but cannot delegate semantics to
backend-specific ignore or configuration files.

## Acceptance

- Search exclusions, generated defaults, default limits, and structural rule
  directories bind through typed settings.
- Explicit CLI scope overrides configurable defaults without erasing their
  reported source.

## Verification

- Search fixtures run with default, configured, overridden, external, and
  invalid path settings.

## Dependencies

- `MVP-E10-S04`
- `MVP-E10-S05`
- `MVP-E03-S01`
