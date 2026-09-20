# MVP-E05-S12 — Analyze Impact

## Outcome

`graph impact` summarizes bounded, statically supported change impact of one
evaluated project or exact code entity.

## Design

- [Project and code graph](../../design/semantics-and-graph.md#project-and-code-graph)

## Boundary

Code targets resolve once and compose supported references, implementations,
callers, and callees through one operation-scoped semantic session. Project
targets use evaluated project-reference evidence only. Candidate tests are
heuristics selected only from affected projects that carry an evaluated known
test-framework package reference; their selection reason is emitted. Impact
does not claim complete runtime knowledge.

## Acceptance

- Output summarizes affected projects, documents, candidate tests,
  public-surface impact, important reverse project paths, and the supported
  semantic relationships for a code target.
- Project targets keep code relationship and public-surface sections explicitly
  inapplicable rather than synthesizing code evidence.
- Limits, depth bounds, and partial coverage state what was not expanded.

## Verification

- Impact fixtures cover private and public changes, project dependencies,
  callers, inheritance, tests, depth limits, and broken projects.

## Dependencies

- `MVP-E05-S02`
- `MVP-E05-S03`
- `MVP-E05-S04`
- `MVP-E05-S05`
- `MVP-E05-S06`
- `MVP-E05-S07`
- `MVP-E05-S08`
- `MVP-E05-S09`
