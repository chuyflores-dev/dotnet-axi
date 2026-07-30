# MVP-E05-S03 — Find Implementations

## Outcome

`search implementations` returns exact compiler implementations for one
interface or abstract target.

## Design

- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

Static results do not claim reflection, dynamic loading, or runtime-generated
implementations.

## Acceptance

- Interface and abstract-member implementations preserve exact symbol identity
  and owner/framework variants.
- Default and complete modes report the full declared analysis scope and every
  coverage failure.

## Verification

- Roslyn oracle fixtures cover interfaces, abstract members, generic types,
  explicit implementations, multi-targeting, and broken dependents.

## Dependencies

- `MVP-E05-S01`
- `MVP-E02-S05`
- `MVP-E02-S06`
