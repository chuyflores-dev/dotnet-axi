# MVP-E03-S07 — Search Structural Patterns

## Outcome

`search structural` returns syntax candidates for raw patterns and configured
rules through the stable output contract.

## Design

- [Structural search](../../design/search-and-context.md#structural-search)

## Boundary

Syntax candidates are never labeled compiler-verified, and AST-grep rewrites
cannot modify source in the MVP.

## Acceptance

- Pattern/rule selection, include/exclude scope, limits, cancellation, empty
  results, and capability failures behave consistently.
- Results preserve normalized locations and candidate provenance.

## Verification

- Command fixtures cover patterns, rules, ignores, limits, no matches,
  unsupported adapters, and attempted rewrites.

## Dependencies

- `MVP-E03-S06`
- `MVP-E01-S06`
