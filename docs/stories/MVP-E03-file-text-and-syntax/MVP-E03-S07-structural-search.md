# MVP-E03-S07 — Defer General Structural Patterns

## Outcome

The MVP omits a general raw-pattern and rule language. Supported syntax shapes
are exposed only through stable tool-owned Roslyn queries.

## Design

- [Structural search](../../design/search-and-context.md#structural-search)

## Boundary

Backend-specific pattern syntax, arbitrary syntax-rule execution, and syntax
rewrites are outside the MVP. Syntax candidates are never labeled
compiler-verified.

## Acceptance

- The CLI does not advertise `search structural --pattern` or `--rule`.
- Supported syntax searches preserve normalized locations, deterministic
  scope, cancellation, limits, empty results, and candidate provenance.

## Verification

- Command-contract tests cover the supported stable syntax-query surface and
  reject unsupported general structural commands without invoking a backend.

## Dependencies

- None.
