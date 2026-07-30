# MVP-E03-S08 — Provide the Roslyn Syntax Engine

## Outcome

Tool-owned syntax queries can parse selected C# files through a stable Roslyn
syntax engine when AST-grep is absent or unsuitable.

## Design

- [Structural search](../../design/search-and-context.md#structural-search)
- [System architecture](../../design/foundations.md#system-architecture)

## Boundary

This story supplies parsing, traversal, normalized candidates, and the query
extension point; individual query semantics belong to separate stories.

## Acceptance

- Selected files parse without loading a compilation or executing repository
  code.
- Candidate locations, cancellation, malformed syntax, generated-code scope,
  and empty results follow the shared contracts.

## Verification

- Engine tests cover valid and malformed C#, cancellation, path scope,
  generated files, deterministic traversal, and normalized coordinates.

## Dependencies

- `MVP-E03-S01`
- `MVP-E02-S07`
