# MVP-E03-S11 — Search Object Creation

## Outcome

`search syntax object-creation --type <name>` returns stable object-creation
candidates.

## Design

- [Stable syntax queries](../../design/search-and-context.md#stable-syntax-queries)

## Boundary

Type matching remains syntactic until an explicit semantic verifier resolves
the constructed type.

## Acceptance

- Explicit, target-typed, generic, qualified, array, and malformed creation
  shapes follow the documented query semantics.
- Roslyn produces deterministic normalized candidates where syntax exposes
  the requested name.

## Verification

- Roslyn fixtures cover creation forms, type filters, unresolved
  target-typed candidates, generated scope, and empty results.

## Dependencies

- `MVP-E03-S08`
