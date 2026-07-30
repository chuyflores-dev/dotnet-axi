# MVP-E05-S04 — Find Overrides

## Outcome

`search overrides` returns exact compiler override relationships for one
virtual or abstract member.

## Design

- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

Hidden members and conventionally similar methods are not reported as
compiler overrides.

## Acceptance

- Direct and transitive overrides preserve exact member identity,
  inheritance path, owner/framework variant, and declared scope.
- Default and complete modes disclose every failed or remaining legal scope.

## Verification

- Roslyn oracle fixtures cover abstract/virtual members, sealed overrides,
  generics, hidden members, multi-targeting, and broken derived projects.

## Dependencies

- `MVP-E05-S01`
- `MVP-E02-S05`
- `MVP-E02-S06`
