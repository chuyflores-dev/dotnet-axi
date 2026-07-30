# MVP-E03 — File, Text, and Syntax Discovery

## Outcome

Agents can find files and source candidates by path, text, regular expression,
syntax shape, and stable tool-owned syntax queries without a persistent index.

## Scope

- Shared traversal, ignore, generated-code, and path-scope behavior.
- Built-in file and text search, including encoding and timeout handling.
- AST-grep structural discovery behind a stable adapter.
- Roslyn syntax fallback and stable syntax queries.
- Deterministic limits, ordering, explicit empty results, and capability
  degradation.

## Boundary

Compiler verification of candidates and symbol identity belong to `MVP-E04`.

## Design

- [Search and context](../../design/search-and-context.md)
- [Design foundations](../../design/foundations.md)
- [Runtime and distribution](../../design/runtime-and-distribution.md)

## Dependencies

- `MVP-E01`
- `MVP-E02`

## Stories

- [MVP-E03-S01 — Traverse source consistently](MVP-E03-S01-shared-traversal.md)
- [MVP-E03-S02 — Search files](MVP-E03-S02-file-search.md)
- [MVP-E03-S03 — Search literal text](MVP-E03-S03-literal-text-search.md)
- [MVP-E03-S04 — Search regular expressions](MVP-E03-S04-regex-search.md)
- [MVP-E03-S05 — Accelerate text search with `rg`](MVP-E03-S05-rg-acceleration.md)
- [MVP-E03-S06 — Adapt AST-grep](MVP-E03-S06-ast-grep-adapter.md)
- [MVP-E03-S07 — Search structural patterns](MVP-E03-S07-structural-search.md)
- [MVP-E03-S08 — Provide the Roslyn syntax engine](MVP-E03-S08-roslyn-syntax-engine.md)
- [MVP-E03-S09 — Search invocation syntax](MVP-E03-S09-invocation-syntax.md)
- [MVP-E03-S10 — Search attributed classes](MVP-E03-S10-attributed-class-syntax.md)
- [MVP-E03-S11 — Search object creation](MVP-E03-S11-object-creation-syntax.md)
- [MVP-E03-S12 — Search catch clauses](MVP-E03-S12-catch-syntax.md)

## Complete when

- File, text, and supported syntax searches work from a clean first process.
- Built-in behavior remains authoritative when optional accelerators are
  absent or incompatible.
