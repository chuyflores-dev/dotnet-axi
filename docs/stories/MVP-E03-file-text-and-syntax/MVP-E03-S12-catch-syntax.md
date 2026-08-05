# MVP-E03-S12 — Search Catch Clauses

## Outcome

`search syntax catch` returns stable catch-clause candidates filtered by type
and empty-body intent.

## Design

- [Stable syntax queries](../../design/search-and-context.md#stable-syntax-queries)

## Boundary

Exception type matching is syntactic until semantic verification is requested.

## Acceptance

- Typed, untyped, filtered, empty, comment-only, and malformed catches follow
  explicit query semantics.
- Roslyn produces deterministic normalized candidates for every supported
  catch shape.

## Verification

- Roslyn fixtures cover type and empty filters, trivia, nested catches,
  generated scope, false candidates, and empty results.

## Dependencies

- `MVP-E03-S08`
