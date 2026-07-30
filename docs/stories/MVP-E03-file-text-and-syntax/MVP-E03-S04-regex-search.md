# MVP-E03-S04 — Search Regular Expressions

## Outcome

`search text --regex` uses the documented .NET regular-expression semantics
with bounded per-file execution.

## Design

- [Regular expressions](../../design/search-and-context.md#regular-expressions)

## Boundary

Invalid patterns and individual file timeouts are structured query outcomes,
not unhandled exceptions.

## Acceptance

- Culture-invariant matching and configured timeouts are enforced.
- Errors identify the query and affected file without a stack trace.

## Verification

- Tests cover valid patterns, invalid patterns, catastrophic input timeouts,
  case modes, and continued scanning.

## Dependencies

- `MVP-E03-S03`
- `MVP-E01-S05`
