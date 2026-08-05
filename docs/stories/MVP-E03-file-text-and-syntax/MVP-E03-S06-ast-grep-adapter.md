# MVP-E03-S06 — Defer AST-grep Beyond MVP

## Outcome

The MVP uses in-process Roslyn syntax for C# syntax discovery and has no
AST-grep runtime, adapter, or distribution contract.

## Design

- [Structural search](../../design/search-and-context.md#structural-search)
- [Required and optional dependencies](../../design/runtime-and-distribution.md#required-and-optional-dependencies)

## Boundary

Raw AST-grep patterns, process invocation, JSON translation, version support,
grammar compatibility, and sidecar distribution are outside the MVP. A future
adapter requires benchmark evidence and a stable product-owned contract.

## Acceptance

- The production solution contains no AST-grep adapter, executable dependency,
  capability surface, or adapter-only test project.
- MVP requirements, design references, and dependent stories identify Roslyn
  syntax as the sole C# syntax engine.
- General backend-specific pattern and rule syntax is not exposed by the MVP.

## Verification

- Canonical restore, build, and test pass without AST-grep installed.
- A repository reference audit finds no MVP AST-grep contract outside this
  explicit deferral work item.

## Dependencies

- None.
