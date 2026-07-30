# MVP-E05-S07 — Find Callees

## Outcome

`search callees` returns statically supported targets invoked by one resolved
member.

## Design

- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

Direct targets, possible virtual/interface targets, convention candidates, and
runtime-unknown invocations remain distinct.

## Acceptance

- Each call site retains its resolved or possible targets, relationship kind,
  resolution, confidence, and analyzed scope.
- Dynamic, delegate, reflection, unresolved, and failed-compilation cases do
  not become false verified targets.

## Verification

- Oracle fixtures cover overloads, extension methods, delegates,
  virtual/interface dispatch, dynamic calls, unresolved calls, and broken
  projects.

## Dependencies

- `MVP-E05-S01`
- `MVP-E02-S05`
- `MVP-E02-S06`
