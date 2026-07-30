# MVP-E05-S01 — Resolve a Semantic Target

## Outcome

Semantic relationship commands resolve exactly one symbol before traversing
compiler relationships.

## Design

- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

Ambiguous names return candidates and require an entity ID or fully qualified
correction; no relationship command guesses.

## Acceptance

- Entity IDs, fully qualified names, and supported declaration queries resolve
  consistently across project/framework variants.
- Missing, ambiguous, stale, and unsupported targets return structured
  corrections before traversal.

## Verification

- Resolver fixtures cover overloads, aliases, partial types, linked files,
  stale IDs, variants, and ambiguity.

## Dependencies

- `MVP-E04-S01`
- `MVP-E04-S02`
- `MVP-E04-S03`
