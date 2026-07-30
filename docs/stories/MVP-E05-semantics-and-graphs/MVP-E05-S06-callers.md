# MVP-E05-S06 — Find Callers

## Outcome

`search callers` returns statically supported call sites for one resolved
target.

## Design

- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

Direct calls, possible virtual/interface dispatch, convention candidates, and
runtime-unknown invocation remain distinct.

## Acceptance

- Dependency-aware scope excludes projects that cannot call the target and
  complete mode expands every legal static scope.
- Each caller retains the call site, containing symbol, target relationship,
  resolution, confidence, and coverage.

## Verification

- Oracle fixtures cover overload resolution, extension methods, delegates,
  virtual/interface dispatch, dynamic calls, project dependencies, and broken
  callers.

## Dependencies

- `MVP-E05-S01`
- `MVP-E02-S05`
- `MVP-E02-S06`
