# MVP-E03-S06 — Adapt AST-grep

## Outcome

The structural engine can invoke a compatible AST-grep process and translate
its JSON into stable internal candidates.

## Design

- [Structural search](../../design/search-and-context.md#structural-search)
- [Required and optional dependencies](../../design/runtime-and-distribution.md#required-and-optional-dependencies)

## Boundary

Raw backend JSON, coordinates, diagnostics, exit codes, and rewrite behavior
never escape the adapter.

## Acceptance

- Version/capability checks, argument-list invocation, cancellation, shared
  traversal, coordinate conversion, and no-match translation are enforced.
- Missing or incompatible AST-grep returns an actionable capability result.

## Verification

- Adapter contract tests use supported, missing, incompatible, malformed, and
  cancelled backend fixtures.

## Dependencies

- `MVP-E03-S01`
- `MVP-E08-S02`
