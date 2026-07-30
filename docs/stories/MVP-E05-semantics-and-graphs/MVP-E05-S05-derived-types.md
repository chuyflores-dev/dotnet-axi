# MVP-E05-S05 — Find Derived Types

## Outcome

`search derived` returns exact compiler-derived types for one class or
interface target.

## Design

- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

Runtime-generated, dynamically loaded, and reflection-only types remain
outside declared static coverage.

## Acceptance

- Direct and transitive derived relationships preserve type identity,
  inheritance path, owner/framework variant, and scope.
- Default and complete modes disclose every failed or remaining legal scope.

## Verification

- Roslyn oracle fixtures cover classes, interfaces, generics, nested types,
  multi-targeting, inaccessible projects, and broken descendants.

## Dependencies

- `MVP-E05-S01`
- `MVP-E02-S05`
- `MVP-E02-S06`
