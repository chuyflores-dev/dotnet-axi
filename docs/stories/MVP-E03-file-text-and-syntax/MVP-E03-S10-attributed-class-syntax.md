# MVP-E03-S10 — Search Attributed Classes

## Outcome

`search syntax class --attribute <name>` returns stable attributed-class
candidates.

## Design

- [Stable syntax queries](../../design/search-and-context.md#stable-syntax-queries)

## Boundary

Attribute name matching remains syntactic until explicitly verified against a
compiler symbol.

## Acceptance

- Qualified, suffixed, multiple, targeted, and malformed attribute syntax is
  handled consistently.
- Roslyn produces deterministic normalized candidates for every supported
  attribute shape.

## Verification

- Roslyn fixtures cover attribute forms, class kinds, filters,
  generated-code scope, false candidates, and empty results.

## Dependencies

- `MVP-E03-S08`
