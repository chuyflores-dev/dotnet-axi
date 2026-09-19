# MVP-E05-S11 — Find Graph Paths

## Outcome

`graph path` finds bounded, deterministic project-reference paths between
selected evaluated projects.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

The first operation version traverses only directed, evaluated
`project-reference` relationships. Project paths are workspace-relative
project-file endpoints in the selected graph; code-entity and mixed-edge paths
remain deferred until their relationship composition has accepted authority.
Path results include only materialized edges and never imply missing runtime
relationships were disproven.

## Acceptance

- Selected evaluated projects can be endpoints.
- Shortest paths and equal-length ties are deterministic, bounded, and retain
  relationship provenance.
- Depth limits and presentation limits are explicit. A deterministic safety
  cap reports an unknown total and requires callers to narrow the graph. A
  no-path result is a verified absence only when the selected graph coverage
  is complete and the depth bound did not stop traversal.
- Partial evaluation retains failures and variant coverage.

## Verification

- Graph fixtures cover shortest paths, ties, cycles, depth limits, no path,
  and partial scope.

## Dependencies

- `MVP-E05-S02`
- `MVP-E05-S03`
- `MVP-E05-S04`
- `MVP-E05-S05`
- `MVP-E05-S06`
- `MVP-E05-S07`
- `MVP-E05-S08`
- `MVP-E05-S09`
