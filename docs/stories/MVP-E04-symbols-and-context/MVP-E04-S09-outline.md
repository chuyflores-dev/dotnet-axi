# MVP-E04-S09 — Outline Source

## Outcome

`outline` returns the stable syntax structure of one document or resolved
symbol.

## Design

- [Show and outline](../../design/search-and-context.md#show-and-outline)

## Boundary

Outline reports syntax structure and never requires a complete compilation.

## Acceptance

- Output includes imports, namespace, types, members, signatures, and relevant
  attributes in source order.
- Roslyn syntax produces the outline without an optional external engine.

## Verification

- Fixtures cover top-level code, file/block namespaces, nested/partial types,
  attributes, malformed syntax, deterministic locations, and symbol-selected
  scope.

## Dependencies

- `MVP-E03-S08`
- `MVP-E04-S02`
- `MVP-E02-S07`
