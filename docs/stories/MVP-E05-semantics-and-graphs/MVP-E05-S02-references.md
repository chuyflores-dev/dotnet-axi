# MVP-E05-S02 — Find References

## Outcome

`search references` returns exact Roslyn references with dependency-aware scope
and explicit completeness.

## Design

- [Compiler-semantic relationships](../../design/semantics-and-graph.md#compiler-semantic-relationships)

## Boundary

Default partial results remain verified but cannot satisfy a mutation that
requires complete reference coverage.

## Acceptance

- Candidate projects are limited by the evaluated project graph and expanded
  for `--complete`.
- Failed projects, frameworks considered, analyzed, and remaining are
  disclosed.

## Verification

- Roslyn oracle tests cover overloads, aliases, linked files, multi-targeting,
  broken projects, and partial/complete scope.

## Dependencies

- `MVP-E05-S01`
- `MVP-E02-S05`
- `MVP-E02-S06`
