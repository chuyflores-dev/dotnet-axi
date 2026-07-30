# MVP-E05 — Semantic Relationships and Graphs

## Outcome

Agents can inspect exact compiler-semantic relationships and on-demand project
or code graphs with explicit scope and completeness.

## Scope

- References, implementations, overrides, derived types, callers, and callees.
- Dependency-aware candidate expansion and complete-scope analysis.
- Project dependencies, cycles, paths, and impact queries.
- Mixed-evidence graph nodes and edges with provenance and confidence.

## Boundary

The MVP computes relationships on demand and does not introduce a persistent
semantic or graph database.

## Design

- [Semantics and graph](../../design/semantics-and-graph.md)
- [Workspace](../../design/workspace.md)
- [Design foundations](../../design/foundations.md)

## Dependencies

- `MVP-E02`
- `MVP-E04`

## Stories

- [MVP-E05-S01 — Resolve a semantic target](MVP-E05-S01-semantic-target-resolution.md)
- [MVP-E05-S02 — Find references](MVP-E05-S02-references.md)
- [MVP-E05-S03 — Find implementations](MVP-E05-S03-implementations.md)
- [MVP-E05-S04 — Find overrides](MVP-E05-S04-overrides.md)
- [MVP-E05-S05 — Find derived types](MVP-E05-S05-derived-types.md)
- [MVP-E05-S06 — Find callers](MVP-E05-S06-callers.md)
- [MVP-E05-S07 — Find callees](MVP-E05-S07-callees.md)
- [MVP-E05-S08 — Define graph contracts](MVP-E05-S08-graph-contracts.md)
- [MVP-E05-S09 — Query project dependencies](MVP-E05-S09-project-dependency-graph.md)
- [MVP-E05-S10 — Detect project cycles](MVP-E05-S10-cycle-detection.md)
- [MVP-E05-S11 — Find graph paths](MVP-E05-S11-graph-paths.md)
- [MVP-E05-S12 — Analyze impact](MVP-E05-S12-impact-analysis.md)

## Complete when

- Results match Roslyn and evaluated MSBuild authority within their declared
  scope.
- Partial coverage cannot be mistaken for complete static knowledge.
