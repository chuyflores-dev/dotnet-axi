# MVP-E05-S10 — Detect Project Cycles

## Outcome

`graph cycles` returns every cycle in the selected evaluated project graph in
a deterministic, non-duplicated form.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

This story detects evaluated project-reference cycles, not runtime or
convention-based code cycles.

## Acceptance

- Equivalent rotations and directions are normalized without hiding distinct
  cycles.
- Empty, partial, and failed graph states preserve their evidence and coverage.

## Verification

- Graph tests cover no cycles, self-cycles, overlapping cycles, conditional
  cycles, and failed nodes.

## Dependencies

- `MVP-E05-S09`
