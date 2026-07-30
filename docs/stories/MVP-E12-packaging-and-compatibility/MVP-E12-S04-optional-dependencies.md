# MVP-E12-S04 — Degrade Optional Dependencies Safely

## Outcome

Missing or incompatible Git, `rg`, and AST-grep disable only the features that
cannot use a built-in or non-Git fallback.

## Design

- [Required and optional dependencies](../../design/runtime-and-distribution.md#required-and-optional-dependencies)

## Boundary

Optional accelerators never become universal runtime prerequisites or change
the stable query contract.

## Acceptance

- Git-only operations return capability errors outside Git while non-Git
  discovery remains usable.
- Text and supported syntax queries fall back; unsupported raw structural
  patterns return a concrete capability correction.

## Verification

- Packaged-tool scenarios run with each dependency present, absent,
  incompatible, shadowed, and failing.

## Dependencies

- `MVP-E12-S03`
- `MVP-E03-S05`
- `MVP-E03-S06`
- `MVP-E03-S08`
