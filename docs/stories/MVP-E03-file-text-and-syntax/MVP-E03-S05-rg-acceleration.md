# MVP-E03-S05 — Accelerate Text Search with `rg`

## Outcome

Compatible text queries may use `rg` while preserving the built-in engine's
observable contract.

## Design

- [Text search](../../design/search-and-context.md#text-search)
- [Required and optional dependencies](../../design/runtime-and-distribution.md#required-and-optional-dependencies)

## Boundary

Queries whose matching, encoding, case, line, or traversal semantics cannot be
proven equivalent stay on the built-in engine.

## Acceptance

- Availability and version are detected without making `rg` mandatory.
- Adapter results, no-match behavior, cancellation, limits, and fallback match
  built-in results for the supported subset.

## Verification

- Conformance tests run the same corpus with `rg` present, absent, and
  incompatible.

## Dependencies

- `MVP-E03-S03`
- `MVP-E03-S04`
- `MVP-E08-S02`
