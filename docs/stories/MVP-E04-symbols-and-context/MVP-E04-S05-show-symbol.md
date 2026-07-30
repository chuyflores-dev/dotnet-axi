# MVP-E04-S05 — Show a Symbol

## Outcome

`show symbol` returns bounded declaration detail and cheap relationship
summaries for one resolved symbol.

## Design

- [Show and outline](../../design/search-and-context.md#show-and-outline)

## Boundary

The command does not expand full relationship evidence or dump an unbounded
body.

## Acceptance

- Output includes identity, signature, owner, location, documentation preview,
  applicable body preview, and available cheap summaries.
- Ambiguous, stale, and unsupported symbols retain their structured
  corrections.

## Verification

- Symbol fixtures cover members, types, overloads, documentation, bodyless
  declarations, stale IDs, and preview limits.

## Dependencies

- `MVP-E04-S01`
- `MVP-E04-S02`
- `MVP-E01-S06`
