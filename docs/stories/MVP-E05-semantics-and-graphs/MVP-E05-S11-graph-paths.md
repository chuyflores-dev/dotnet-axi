# MVP-E05-S11 — Find Graph Paths

## Outcome

`graph path` finds bounded, deterministic relationship paths between supported
entities.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

Path results include only materialized or on-demand discoverable edges and
never imply missing runtime relationships were disproven.

## Acceptance

- Project and supported code entities can be endpoints.
- Depth limits, no-path results, mixed evidence, partial expansion, and
  provenance are explicit.

## Verification

- Graph fixtures cover shortest paths, ties, cycles, depth limits, no path,
  mixed edge kinds, and partial scope.

## Dependencies

- `MVP-E05-S02`
- `MVP-E05-S03`
- `MVP-E05-S04`
- `MVP-E05-S05`
- `MVP-E05-S06`
- `MVP-E05-S07`
- `MVP-E05-S08`
- `MVP-E05-S09`
